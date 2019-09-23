using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using Newtonsoft.Json.Linq;

namespace CcWorks.Workers
{
    public static class ReviewWorker
    {
        private const string QueryGetPRDetailsFormat = @"query {{
                repository(owner:""trilogy-group"", name:""{0}""){{
                    pullRequest(number: {1}){{
                        id,
                        bodyHTML,
                        assignees(first: 20) {{ nodes {{ id }} }}
                        labels(last: 20) {{ nodes {{ 
                                                id
                                                name
                                            }}
                                        }}
                                    }}
                                }}
                            }}";

        private const string QueryGetAssignedLabelsFormat = @"query{{
                    repository(owner: ""{0}"", name: ""{1}""){{
                        pullRequest(number: {2}){{
                            labels(last: 20){{
                                nodes {{
                                    id
                                    name
                                }}
                            }}
                        }}
                    }}
                }}";

        private const string QueryGetRepoLabelsFormat = @"query{{
            repository(owner: ""{0}"", name: ""{1}""){{
                labels(first: 20){{
                    nodes{{
                        id
                        name
                    }}
                }}
            }}
        }}";

        private const string MutationAddAssigneePRFormat = @"mutation
                    {{
                      addAssigneesToAssignable(input: {{
                        assignableId: ""{0}"", 
                        assigneeIds: [""{1}""]}}) {{ clientMutationId }}}}";

        private const string MutationClearLabelsPRFormat = @"mutation{{
                        clearLabelsFromLabelable(
                        input:{{labelableId:""{0}""}}) {{clientMutationId}}
                        }}";

        private const string MutationAddLabelsFormat = @"mutation{{
                        addLabelsToLabelable(
                        input:{{labelableId: ""{0}"",labelIds: ""{1}""}}) {{clientMutationId}}
                        }}";

        private const string LabelCRNReviewCompleted = "CRN Review Completed";

        public static async Task DoWork(ReviewCommandSettings settings, CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            var prUrl = parameters.Get("PR url: ");
            GithubHelper.ParsePrUrl(prUrl, out var repoName, out var prNumber);

            var timeSpent = parameters.Get("Time spent: ");

            if (string.IsNullOrWhiteSpace(timeSpent) || timeSpent.Trim() == "0")
            {
                throw new CcException("Time spent should not be empty");
            }

            //var coverage = parameters.Get("Coverage percentage: ");
            //if (string.IsNullOrWhiteSpace(coverage))
            //{
            //    throw new CcException("Coverage should not be empty");
            //}

            Console.Write("Getting PR... ");
            var prDetails = string.Format(QueryGetPRDetailsFormat, repoName, prNumber);
            var prInfo = await GithubHelper.Query(prDetails, commonSettings.GithubToken);
            var jPullRequest = prInfo["repository"]["pullRequest"];
            var bodyHtml = jPullRequest["bodyHTML"].Value<string>();
            Console.WriteLine("done");
            Console.Write("Getting ticket... ");
            var regex = new Regex(@"https://jira\.devfactory\.com/browse/(CC-\d+)");
            var m = regex.Match(bodyHtml);

            if (!m.Success)
            {
                throw new CcException("Ticket not found");
            }

            var issue = await jira.Issues.GetIssueAsync(m.Groups[1].Value);
            Console.WriteLine("done");

            var issueStatus = issue.Status.ToString();

            if (!issueStatus.Equals("In progress", StringComparison.InvariantCultureIgnoreCase))
            {
                if (issueStatus.Equals("Ready for review", StringComparison.OrdinalIgnoreCase))
                {
                    await AssignLabelToPR(commonSettings, repoName, prNumber, jPullRequest["id"].Value<string>());
                }

                throw new CcException("Ticket should be in \"In progress\" state");
            }

            var repoSettings = SettingsHelper.GetRepoSettings(commonSettings, repoName);

            if (string.IsNullOrWhiteSpace(repoSettings?.Pca))
            {
                throw new CcException($"PCA isn't defined for repo \"{repoName}\" in settings.json");
            }

            Console.Write("Moving to review... ");
            SetCustomField(issue, "Reviewer", repoSettings.Pca);
            SetCustomField(issue, "Code Review Ticket URL", prUrl);
            await issue.WorkflowTransitionAsync("Submit for Review", new WorkflowTransitionUpdates()
            {
                TimeSpent = timeSpent
            });
            Console.WriteLine("done");

            if (settings.AssignPr)
            {
                Console.Write("Assigning PR... ");

                var currentUserIdJson = await GithubHelper.Query("query {viewer {id}}", commonSettings.GithubToken);
                var userId = currentUserIdJson["viewer"]["id"].Value<string>();
                var nodes = jPullRequest["assignees"]["nodes"] as JArray ?? new JArray();

                if (nodes.Any(node => node["id"].Value<string>() == userId))
                {
                    Console.WriteLine("already assigned");
                }
                else
                {
                    var assignPrMutation = string.Format(MutationAddAssigneePRFormat, jPullRequest["id"].Value<string>(), userId);
                    await GithubHelper.Query(assignPrMutation, commonSettings.GithubToken);
                    Console.WriteLine("done");
                }

                await AssignLabelToPR(commonSettings, repoName, prNumber, jPullRequest["id"].Value<string>());
            }
        }

        private static async Task AssignLabelToPR(CommonSettings commonSettings, string repoName, string prNumber, string prId)
        {
            Console.Write("Get CRN review label id...");

            var getRepoLabels = string.Format(QueryGetRepoLabelsFormat, "trilogy-group", repoName);
            var repoLabels = await GithubHelper.Query(getRepoLabels, commonSettings.GithubToken);
            var repoLabelsNode = repoLabels["repository"]["labels"]["nodes"] as JArray;

            if (repoLabelsNode == null)
            {
                Console.Write("No labels found in repo");
                return;
            }

            var crnReviewLabelId = repoLabelsNode.First(
                    x => x["name"].Value<string>().Equals(LabelCRNReviewCompleted, StringComparison.OrdinalIgnoreCase))
                ["id"]
                .Value<string>();

            Console.WriteLine("done");

            Console.Write("Get PR Assigned Labels...");
            var getAssignedLabelsQuery = string.Format(QueryGetAssignedLabelsFormat, "trilogy-group", repoName, prNumber);
            var assignedLabels = await GithubHelper.Query(getAssignedLabelsQuery, commonSettings.GithubToken);
            var labelNodes = assignedLabels["repository"]["pullRequest"]["labels"]["nodes"] as JArray ?? new JArray();
            Console.WriteLine("done");

            if (labelNodes.Any())
            {
                if (labelNodes.Any(x => x["id"].Value<string>().Equals(crnReviewLabelId)))
                {
                    Console.Write($"{LabelCRNReviewCompleted} already assigned");
                    Console.WriteLine("done");
                    return;
                }

                Console.Write("Clear labels...");
                var mutationClearLabels = string.Format(MutationClearLabelsPRFormat, prId);
                await GithubHelper.Query(mutationClearLabels, commonSettings.GithubToken);
                Console.WriteLine("done");
            }

            Console.Write("Add label...");
            var mutationAddLabels = string.Format(MutationAddLabelsFormat, prId, crnReviewLabelId);
            await GithubHelper.Query(mutationAddLabels, commonSettings.GithubToken);
            Console.WriteLine("done");
        }

        private static void SetCustomField(Issue issue, string name, string value)
        {
            if (issue.CustomFields.SingleOrDefault(f => f.Name == name) == null)
            {
                issue.CustomFields.Add(name, value);
            }
            else
            {
                issue[name] = value;
            }
        }
    }
}