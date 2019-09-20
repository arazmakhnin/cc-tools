using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using Newtonsoft.Json;
using JiraHelper = CcWorks.Helpers.JiraHelper;

namespace CcWorks.Workers
{
    public static class NewWorker
    {
        public static async Task DoWork(NewCommandSettings settings, CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            if (parameters.Any())
            {
                throw new CcException("Command \"new\" doesn't support parameters yet");
            }

            var shortTicketType = parameters.Get("Enter ticket type: ");
            var longTicketType = GetTicketType(shortTicketType);
            string brpQualifier = string.Empty;
            if (longTicketType == "BRP Issues")
            {
                brpQualifier = parameters.Get("Please specify BRP type [m = Magic Strings; f = Formatting]: ");
                brpQualifier = " - " + GetBrpQualifier(brpQualifier);
            }

            var fileParts = ReadFileParts(longTicketType);

            var sourceTicketUrl = parameters.Get("Enter source Jira ticket: ");
            var ticketKey = JiraHelper.GetIssueKey(sourceTicketUrl);

            var strLinkTickets = parameters.Get("Link this ticket to the new one? [Y/n]: ");
            var linkTickets = string.IsNullOrWhiteSpace(strLinkTickets) ||
                              strLinkTickets.Equals("y", StringComparison.OrdinalIgnoreCase);

            Console.Write("Get ticket... ");
            var issue = await jira.Issues.GetIssueAsync(ticketKey);
            Console.WriteLine("done");

            var scmUrl = issue.GetScmUrl();
            var repoName = scmUrl.Split('/')[4];
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repoName = Path.GetFileNameWithoutExtension(repoName);
            }

            var repoSettings = SettingsHelper.GetRepoSettings(commonSettings, repoName);
            var mainBranch = SettingsHelper.GetMainBranch(repoSettings);
            var currentBranch = GitHelper.GetCurrentBranch(repoSettings, commonSettings.ProjectsPath);
            if (currentBranch != mainBranch)
            {
                ConsoleHelper.WriteColor(
                    $"Your current branch is {currentBranch}, but usually it should be {mainBranch}. Do you really want to create new ticket from this branch? [y/N]: ",
                    ConsoleColor.Yellow);
                var answer = Console.ReadLine() ?? string.Empty;
                if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            Console.Write("Create new issue... ");
            var newIssue = jira.CreateIssue("CC");
            newIssue.Summary = longTicketType + brpQualifier + " :: " + GetFileName(fileParts.First().FileName, commonSettings.ProjectsPath, repoSettings.ActualFolderName);
            newIssue.Type = longTicketType;
            newIssue.Assignee = commonSettings.JiraUserName;
            newIssue.Priority = issue.Priority;
            newIssue.Labels.AddRange(issue.Labels);

            const string formattingLabel = "Formatting";
            if (brpQualifier == " - Formatting" && !issue.Labels.Contains(formattingLabel))
            {
                newIssue.Labels.Add(formattingLabel);
            }

            var scmBranch = GetScmBranchField(currentBranch, repoSettings, commonSettings);
            newIssue.CustomFields.Add("scm_branch", scmBranch);

            foreach (var field in settings.FieldsToCopy.Where(f => f != "scm_branch"))
            {
                CopyCustomField(issue, newIssue, field);
            }
            
            newIssue.Description = GetDescription(mainBranch, fileParts, commonSettings.ProjectsPath, repoSettings);
            await newIssue.SaveChangesAsync();
            Console.WriteLine("done");

            if (linkTickets)
            {
                Console.Write("Add link... ");
                await newIssue.LinkToIssueAsync(issue.Key.ToString(), "Relates");
                Console.WriteLine("done");
            }

            Console.WriteLine($"New issue: https://jira.devfactory.com/browse/{newIssue.Key}");
        }

        private static string GetScmBranchField(string currentBranch, RepoSettings repoSettings, CommonSettings commonSettings)
        {
            var commitData = GitHelper.Exec("git rev-parse HEAD", repoSettings, commonSettings.ProjectsPath);
            if (!commitData.Any() || commitData.Count > 1)
            {
                throw new CcException("Current commit hash not found");
            }

            return $"Branch: {currentBranch}\n\nCommit Hash: {commitData.First()}";
        }

        private static List<FilePart> ReadFileParts(string longTicketType)
        {
            var list = new List<FilePart>();

            var lastFileName = string.Empty;

            while (true)
            {
                var prompt = list.Any() ? " [s - same file, \"\" - stop]" : string.Empty;
                Console.Write($"Enter full file name{prompt}: ");
                var fileName = Console.ReadLine() ?? string.Empty;
                if (list.Any() && string.IsNullOrWhiteSpace(fileName))
                {
                    break;
                }

                if (list.Any() && fileName.Trim().Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = lastFileName;
                }

                if (!File.Exists(fileName))
                {
                    throw new CcException("File doesn't exist");
                }

                lastFileName = fileName;

                string startLineStr;
                if (longTicketType == "BRP Issues")
                {
                    startLineStr = "whole";
                }
                else
                {
                    Console.Write("Start line [or \"whole\"]: ");
                    startLineStr = Console.ReadLine() ?? string.Empty;
                }

                if (startLineStr.Trim().Equals("whole"))
                {
                    list.Add(new FilePart(fileName, true, 0, 0));
                }
                else
                {
                    if (!int.TryParse(startLineStr.Trim(), out var startLine))
                    {
                        throw new CcException("Can't parse");
                    }

                    Console.Write("End line: ");
                    var endLineStr = Console.ReadLine() ?? string.Empty;
                    if (!int.TryParse(endLineStr.Trim(), out var endLine))
                    {
                        throw new CcException("Can't parse");
                    }

                    list.Add(new FilePart(fileName, false, startLine, endLine));
                }

                if (longTicketType != "Duplicate Code")
                {
                    break;
                }

                Console.WriteLine("Please enter another duplication");
            }

            return list;
        }

        private static void CopyCustomField(Issue issue, Issue newIssue, string fieldName)
        {
            newIssue.CustomFields.Add(fieldName, issue.CustomFields[fieldName]?.Values);
        }

        private static string GetDescription(string mainBranch, IReadOnlyCollection<FilePart> fileParts, string projectsPath, RepoSettings repoSettings)
        {
            //            const string template = @"h4. Issue Locations 
            //{noformat} [
            //    {
            //        ""endLine"": %end_line%,
            //        ""fileName"": ""%file_name%"",
            //        ""startColumn"": %start_column%,
            //        ""startLine"": %start_line%
            //    }
            //] {noformat} 
            //h4. Code 
            //{code:java}%code%{code}";

            const string template = @"h4. Issue Locations 
{noformat} %fileParts% {noformat} 
h4. Code 
{code:java}%code%{code}";

            var first = fileParts.First();
            IEnumerable<string> contentLines = GetBranchFileContent(mainBranch, first.FileName, projectsPath, repoSettings).ToList();
            if (!first.Whole)
            {
                contentLines = contentLines.Skip(first.StartLine - 1).Take(first.EndLine - first.StartLine + 1);
            }
            else
            {
                first.StartLine = 1;
                first.EndLine = contentLines.Count();
            }

            var content = string.Join("\r\n", contentLines);

            const int maxDescriptionSize = 50 * 1024;
            if (content.Length > maxDescriptionSize)
            {
                content = content.Substring(0, maxDescriptionSize);
            }

            var startColumn = content.TakeWhile(c => c == ' ' || c == '\t').Count();

            var strFileParts = JsonConvert.SerializeObject(fileParts.Select(p => new
            {
                endLine = p.EndLine,
                fileName = GetFileName(p.FileName, projectsPath, repoSettings.ActualFolderName),
                startColumn = startColumn,
                startLine = p.StartLine
            }), Formatting.Indented);

            return template
                .Replace("%fileParts%", strFileParts)
                .Replace("%code%", content);
        }

        private static string GetTicketType(string ticketType)
        {
            switch (ticketType.ToUpperInvariant())
            {
                case "LONG CLASS":
                case "LC":
                    return "Long Classes";

                case "LONG METHOD":
                case "LM":
                    return "Long Methods";

                case "DUPLICATE CODE":
                case "CODE DUPLICATION":
                case "CD":
                case "DUP":
                    return "Duplicate Code";

                case "DEAD CODE":
                case "DEAD":
                case "DC":
                    return "Dead Code";

                case "MEMORY LEAK":
                case "ML":
                    return "Symbolic Execution - Memory Leaks";

                case "BRP":
                    return "BRP Issues";

                default:
                    throw new CcException(
                        $"Unknown ticket type: {ticketType}. Only LC, LM, ML, Dead code (DC, dead) and Code duplication (CD, dup) are supported for now");
            }
        }

        private static string GetBrpQualifier(string shortQualifier)
        {
            switch (shortQualifier.ToUpperInvariant())
            {
                case "F":
                    return "Formatting";
                case "M":
                    return "Magic Strings";
                default:
                    return shortQualifier;
            }
        }

        private static string GetFileName(string fileName, string projectsPath, string repoFolder)
        {
            if (!fileName.StartsWith(projectsPath, StringComparison.CurrentCultureIgnoreCase))
            {
                throw new CcException("File isn't placed in `projectsPath` directory");
            }

            var cutFileName = fileName.Substring(projectsPath.Length).TrimStart('/', '\\');
            if (!cutFileName.StartsWith(repoFolder, StringComparison.CurrentCultureIgnoreCase))
            {
                throw new CcException($"File isn't placed in \"{repoFolder}\" directory");
            }

            return cutFileName.Substring(repoFolder.Length).TrimStart('/', '\\').Replace('\\', '/');
        }

        private static IReadOnlyCollection<string> GetBranchFileContent(
            string branchName,
            string fileName,
            string projectsPath,
            RepoSettings repoSettings)
        {
            return GitHelper.Exec(
                $"git show {branchName}:{fileName}".Replace(
                        Path.Combine(projectsPath, repoSettings.ActualFolderName) + "\\",
                        string.Empty)
                    .Replace("\\", "/"),
                repoSettings,
                projectsPath);
        }
    }

    public class FilePart
    {
        public string FileName { get; }
        public bool Whole { get; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }

        public FilePart(string fileName, bool whole, int startLine, int endLine)
        {
            FileName = fileName;
            Whole = whole;
            StartLine = startLine;
            EndLine = endLine;
        }
    }
}
