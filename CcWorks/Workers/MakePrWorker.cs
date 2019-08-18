using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using Newtonsoft.Json.Linq;
using JiraHelper = CcWorks.Helpers.JiraHelper;

namespace CcWorks.Workers
{
    public static class MakePrWorker
    {
        public static async Task DoWork(PrCommandSettings settings, CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            var ticketUrl = parameters.Get("Enter Jira ticket: ");
            var issueKey = JiraHelper.GetIssueKey(ticketUrl);

            var comment = GetComment(settings, parameters);

            var noteForReviewer = parameters.Get("Note for reviewer: ");
            if (settings.NotesAliases.ContainsKey(noteForReviewer))
            {
                noteForReviewer = settings.NotesAliases[noteForReviewer];
            }

            bool isOnlyPr = false;
            if (parameters.Any())
            {
                var str = parameters.Get(string.Empty);
                if (str == "--onlyPr")
                {
                    isOnlyPr = true;
                }
                else
                {
                    throw new CcException($"Unknown argument: {str}");
                }
            }

            Console.Write("Get ticket... ");
            var issue = await jira.Issues.GetIssueAsync(issueKey);
            var repoUrl = issue.GetScmUrl();
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                Console.WriteLine("Can't get repo name");
                return;
            }

            Console.WriteLine("done");

            var repoName = repoUrl.Split('/')[4];
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repoName = Path.GetFileNameWithoutExtension(repoName);
            }

            var repoSettings = SettingsHelper.GetRepoSettings(commonSettings, repoName);
            var branchPrefix = string.IsNullOrWhiteSpace(repoSettings?.BranchPrefix)
                ? "feature"
                : repoSettings.BranchPrefix;

            var mainBranch = SettingsHelper.GetMainBranch(repoSettings);

            var key = issue.Key.ToString();
            var ticketType = GetTicketType(issue);

            if (!isOnlyPr)
            {
                var stagedFiles = GitHelper.Exec("git diff --name-only --cached", repoSettings, commonSettings.ProjectsPath);
                var changedFiles = GitHelper.Exec("git diff --name-only", repoSettings, commonSettings.ProjectsPath);

                if (!stagedFiles.Any() && !changedFiles.Any())
                {
                    throw new CcException("No changed files found");
                }

                var filesForCheck = stagedFiles.Any() ? stagedFiles : changedFiles;
                if (filesForCheck.Any(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
                {
                    ConsoleHelper.WriteColor("You are going to commit changes in sln-file. Do you really want to do it? [y/N]: ", ConsoleColor.Yellow);
                    var answer = Console.ReadLine() ?? string.Empty;
                    if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                var currentBranch = GitHelper.GetCurrentBranch(repoSettings, commonSettings.ProjectsPath);
                if (currentBranch != mainBranch)
                {
                    ConsoleHelper.WriteColor(
                        $"Your current branch is {currentBranch}, but usually it should be {mainBranch}. Do you really want to create new branch from this one? [y/N]: ",
                        ConsoleColor.Yellow);
                    var answer = Console.ReadLine() ?? string.Empty;
                    if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                Console.Write("Set assignee... ");
                if (issue.Assignee == null)
                {
                    issue.Assignee = commonSettings.JiraUserName;
                    await issue.SaveChangesAsync();
                    Console.WriteLine("done");
                }
                else
                {
                    if (issue.Assignee != commonSettings.JiraUserName)
                    {
                        throw new CcException($"Ticket is already assigned to \"{issue.Assignee}\"!");
                    }
                    else
                    {
                        Console.WriteLine("already assigned");
                    }
                }
                
                Console.Write("Move ticket to \"In progress\"... ");
                if (issue.Status.ToString().Equals("To Do", StringComparison.InvariantCultureIgnoreCase))
                {
                    await issue.WorkflowTransitionAsync("Not False Positive");
                }

                if (issue.Status.ToString().Equals("Writing Test Code", StringComparison.InvariantCultureIgnoreCase))
                {
                    await issue.WorkflowTransitionAsync("Test coverage available");
                }

                if (issue.Status.ToString().Equals("Ready to refactor", StringComparison.InvariantCultureIgnoreCase))
                {
                    await issue.WorkflowTransitionAsync("Start progress");
                }

                Console.WriteLine("done");
            
                Console.Write("Push changes... ");
                var createBranchCommand = $"git checkout -b {branchPrefix}/{key}";
                var commitOption = stagedFiles.Any() ? string.Empty : "a";
                var commitCommand = $"git commit -{commitOption}m  \"{key}: Fixes {ticketType}\"";
                var pushCommand = $"git push --set-upstream origin {branchPrefix}/{key}";
                GitHelper.Exec(
                    $"{createBranchCommand} && {commitCommand} && {pushCommand}",
                    repoSettings,
                    commonSettings.ProjectsPath);

                Console.WriteLine("done");

                Console.Write($"Checkout {mainBranch}... ");
                try
                {
                    GitHelper.Exec($"git checkout {mainBranch} && git pull", repoSettings, commonSettings.ProjectsPath);
                    Console.WriteLine("done");
                }
                catch (GitException)
                {
                    ConsoleHelper.WriteLineColor("failed", ConsoleColor.Yellow);
                }

                Console.Write("Waiting... ");
                Thread.Sleep(TimeSpan.FromSeconds(3));
                Console.WriteLine("done");
            }

            Console.Write("Create PR... ");
            var query = @"query { repository(owner:""trilogy-group"", name:""" + repoName + @"""){ id }}";
            var repoData = await GithubHelper.Query(query, commonSettings.GithubToken);
            var repoId = repoData["repository"]["id"].Value<string>();

            var fullNote = string.IsNullOrWhiteSpace(noteForReviewer)
                ? string.Empty
                : $@"# Note for Reviewers\r\n- {noteForReviewer}\r\n\r\n";

            var (testCoverageMessage, coverageWarning) = GetTestCoverageMessage(issue);

            var mutation = @"mutation
                {
                  createPullRequest(input:{
                    title:""[" + key + "] " + issue.Summary + @""", 
                    baseRefName: """ + mainBranch + @""", 
                    body: ""# Links\r\nhttps://jira.devfactory.com/browse/" + key + @"\r\n\r\n# Changes\r\n- " + comment + @"\r\n\r\n" + fullNote + testCoverageMessage + @""", 
                    headRefName: """ + branchPrefix + "/" + key + @""", 
                    repositoryId: """ + repoId + @"""
                  }){
                    pullRequest{
                      id,
                      url
                    }
                  }
                }";

            var resultJson = await GithubHelper.Query(mutation, commonSettings.GithubToken);
            var jPullRequest = resultJson["createPullRequest"]["pullRequest"];
            var prUrl = jPullRequest["url"].Value<string>();

            Console.WriteLine("done");

            if (coverageWarning)
            {
                Console.WriteLine("Please don't forget to fill test coverage manually");
            }

            if (settings.AssignPr)
            {
                Console.Write("Assign PR... ");

                var prId = jPullRequest["id"].Value<string>();
                
                var currentUserIdJson = await GithubHelper.Query("query {viewer {id}}", commonSettings.GithubToken);
                var userId = currentUserIdJson["viewer"]["id"].Value<string>();

                var assignPrMutation = @"mutation
                    {
                      addAssigneesToAssignable(input: {
                        assignableId: """ + prId + @""", 
                        assigneeIds: [""" + userId + @"""]}) { clientMutationId }}";

                await GithubHelper.Query(assignPrMutation, commonSettings.GithubToken);

                Console.WriteLine("done");
            }

            Console.Write("Write PR url to ticket... ");
            const string codeReviewUrl = "Code Review Ticket URL";
            if (issue.CustomFields.Any(f => f.Name == codeReviewUrl))
            {
                issue.CustomFields[codeReviewUrl].Values = new [] { prUrl };
            }
            else
            {
                issue.CustomFields.Add(codeReviewUrl, prUrl);
            }

            issue.SaveChanges();
            Console.WriteLine("done");

            Console.WriteLine("New PR url: " + prUrl);
        }

        private static (string, bool) GetTestCoverageMessage(Issue issue)
        {
            var coverageWarning = true;

            var message = string.Empty;
            if (issue.Labels.Contains("BasicService"))
            {
                message = "Tests are not required for `BasicService` tickets.";
                coverageWarning = false;
            }
            else if (issue.Type == "BRP Issues")
            {
                message = "Tests are not required for `BRP` tickets.";
                coverageWarning = false;
            }
            else if (issue.Type == "Symbolic Execution - Memory Leaks")
            {
                message = "Tests are not required for `Memory Leak` tickets.";
                coverageWarning = false;
            }
            else if (issue.Type == "Dead Code")
            {
                message = "Tests are not required for `Dead Code` tickets.";
                coverageWarning = false;
            }

            var testCoverage = $@"# Tests and coverage\r\n- {message}\r\n\r\n";
            return (testCoverage, coverageWarning);
        }

        private static string GetComment(PrCommandSettings settings, Parameters parameters)
        {
            var comment = parameters.Get("Comment: ");

            while (true)
            {
                if (string.IsNullOrWhiteSpace(comment))
                {
                    comment = parameters.Get("Comment: ");
                    continue;
                }
            
                if (settings.CommentAliases.ContainsKey(comment))
                {
                    comment = settings.CommentAliases[comment];
                }
                else if (comment.Length <= 3)
                {
                    Console.Write($"Looks like you entered alias that isn't defined. Do you want to use \"{comment}\" as a comment? [y/n]: ");
                    var y = Console.ReadLine() ?? string.Empty;
                    if (!y.Equals("y", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Write("Then enter comment: ");
                        comment = Console.ReadLine() ?? string.Empty;
                        continue;
                    }
                }

                break;
            }

            comment = comment.Replace("\"", "\\\"");
            return comment;
        }

        private static string GetTicketType(Issue issue)
        {
            switch (issue.Type.ToString().ToLower())
            {
                case "long methods":
                    return "long method";

                case "long classes":
                    return "long class";

                case "duplicate code":
                    return "code duplication";

                case "dead code":
                    return "dead code";

                case "symbolic execution - memory leaks":
                    return "memory leak";

                case "brp issues":
                    return "brp";

                default:
                    throw new InvalidOperationException($"Unknown issue type: {issue.Type}");
            }
        }
    }
}
