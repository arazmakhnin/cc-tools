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
            var query = @"query {
                repository(owner:""trilogy-group"", name:""" + repoName + @"""){
                    pullRequest(number: " + prNumber + @"){
                        id,
                        bodyHTML,
                        assignees(first: 20) { nodes { id } }
                    }
                }  
            }";

            var prInfo = await GithubHelper.Query(query, commonSettings.GithubToken);
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

            if (!issue.Status.ToString().Equals("In progress", StringComparison.InvariantCultureIgnoreCase))
            {
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
                foreach (var node in nodes)
                {
                    if (node["id"].Value<string>() == userId)
                    {
                        Console.WriteLine("already assigned");
                        return;
                    }
                }

                var prId = jPullRequest["id"].Value<string>();
                var assignPrMutation = @"mutation
                    {
                      addAssigneesToAssignable(input: {
                        assignableId: """ + prId + @""", 
                        assigneeIds: [""" + userId + @"""]}) { clientMutationId }}";

                await GithubHelper.Query(assignPrMutation, commonSettings.GithubToken);

                Console.WriteLine("done");
            }
        }

        private static void SetCustomField(Issue issue, string name, string value)
        {
            var field = issue.CustomFields.SingleOrDefault(f => f.Name == name);
            if (field == null)
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