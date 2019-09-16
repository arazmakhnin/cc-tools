using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using Newtonsoft.Json;

namespace CcWorks.Workers
{
    public static class DuplicateTicketChecker
    {
        public static async Task DoWork(
            DuplicateTicketCommandSettings settings,
            Parameters parameters,
            Jira jira)
        {
            if (parameters.Any())
            {
                throw new CcException("Command \"ticket\" doesn't support parameters yet");
            }

            var ticketUrl = parameters.Get("Enter ticket address: ");

            var ticketKey = JiraHelper.GetIssueKey(ticketUrl);

            bool sameTypeOnly;
            if (settings.CheckSameTypeOnly.HasValue)
            {
                sameTypeOnly = settings.CheckSameTypeOnly.Value;
            }
            else
            {
                var considerOtherTicketsTypes = parameters.Get("Consider only tickets of same type? [Y/N] : ");
                sameTypeOnly = string.IsNullOrWhiteSpace(considerOtherTicketsTypes)
                    || considerOtherTicketsTypes.Equals("y", StringComparison.OrdinalIgnoreCase)
                    || considerOtherTicketsTypes.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            Console.WriteLine("Getting duplicate tickets report... ");
            var tickets = await GetDuplicatedTickets(ticketKey, sameTypeOnly, jira, settings);


            if (tickets.Any())
            {
                ConsoleHelper.WriteColor("Here are the possible duplications:\n\n", ConsoleColor.DarkYellow);
                tickets.ForEach(issue => ConsoleHelper.WriteColor($"https://jira.devfactory.com/browse/{issue.Key}\n", ConsoleColor.Yellow));
            }
            else
            {
                Console.WriteLine("No duplications found!");
            }
        }

        public static async Task<List<Issue>> GetDuplicatedTickets(
            string ticketKey,
            bool sameTypeOnly,
            Jira jira,
            DuplicateTicketCommandSettings settings)
        {
            var issue = await jira.Issues.GetIssueAsync(ticketKey);

            var queryFiles = GetIssueLocationsFromTicket(issue);

            var fileQuery = new StringBuilder();

            queryFiles.ForEach(
                locations => { fileQuery.Append($@"and description ~""{locations.FileName}"" "); });

            var openStatuses = @"AND status in (""in progress"", ""ready to refactor"", ""ready for review"", ""code review"", ""code merge"")";
            var jql = $@"Project = CC {openStatuses} {fileQuery} Order By Updated DESC";
            var issues = await jira.Issues.GetIssuesFromJqlAsync(jql) ;
            return issues.Where(
                    dup => issue.Key.Value != dup.Key.Value
                        && issue.GetScmUrl() == dup.GetScmUrl()
                        && (!sameTypeOnly || issue.Type.Name == dup.Type.Name))
                .Where(
                    dup =>
                    {
                        var locations = GetIssueLocationsFromTicket(dup);
                        var overlap = locations.Any(
                            location => queryFiles.Any(
                                issueLocation =>
                                {
                                    var startLineInsideRange = IsInsideRange(issueLocation.StartLine, location, settings);
                                    var endLineInsideRange = IsInsideRange(issueLocation.EndLine, location, settings);
                                    var otherStartLineInsideRange = IsInsideRange(location.StartLine, issueLocation, settings);
                                    var otherEndLineInsideRange = IsInsideRange(location.EndLine, issueLocation, settings);

                                    return (startLineInsideRange || endLineInsideRange)
                                        || (otherStartLineInsideRange || otherEndLineInsideRange);
                                }));
                        return overlap;
                    })
                .ToList();
        }

        private static bool IsInsideRange(int position, IssueLocation location, DuplicateTicketCommandSettings settings)
        {
            return (position >= location.StartLine + settings.LineOffset
                    || position >= location.StartLine - settings.LineOffset)
                && (position <= location.EndLine + settings.LineOffset
                    || position <= location.EndLine - settings.LineOffset);
        }

        private static List<IssueLocation> GetIssueLocationsFromTicket(Issue issue)
        {
            var fileRegex = new Regex(@"\[([^\]]+)\]");
            var json = fileRegex.Match(issue.Description).Value;
            return JsonConvert.DeserializeObject<List<IssueLocation>>(json);
        }
    }
}