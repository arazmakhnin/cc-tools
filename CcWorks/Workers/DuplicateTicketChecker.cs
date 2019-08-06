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
            var tickets = await GetDuplicatedTickets(ticketKey, sameTypeOnly, jira, settings.LineOffset);


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
            int lineOffset,
            DetectionMethod method = DetectionMethod.Overlap)
        {
            var issue = await jira.Issues.GetIssueAsync(ticketKey);

            var queryFiles = LocationHelper.GetIssueLocationsFromTicket(issue);

            var fileQuery = new StringBuilder();

            queryFiles.ForEach(locations => { fileQuery.Append($@"and description ~""{locations.FileName}"" "); });

            var openStatuses =
                @"AND status in (""in progress"", ""ready to refactor"", ""ready for review"", ""code review"", ""code merge"")";
            var jql = $@"Project = CC {openStatuses} {fileQuery} Order By Updated DESC";
            var issues = await jira.Issues.GetIssuesFromJqlAsync(jql);
            return issues.Where(
                    dup => issue.Key.Value != dup.Key.Value
                        && issue.GetScmUrl() == dup.GetScmUrl()
                        && (!sameTypeOnly || issue.Type.Name == dup.Type.Name))
                .Where(
                    dup =>
                    {
                        var locations = LocationHelper.GetIssueLocationsFromTicket(dup);
                        if (method == DetectionMethod.Overlap)
                        {
                            var overlap = locations.Any(
                                location => queryFiles.Any(
                                    issueLocation =>
                                    {
                                        var startLineInsideRange = LocationHelper.IsInsideRange(
                                            issueLocation.StartLine,
                                            location,
                                            lineOffset);
                                        var endLineInsideRange = LocationHelper.IsInsideRange(
                                            issueLocation.EndLine,
                                            location,
                                            lineOffset);
                                        var otherStartLineInsideRange = LocationHelper.IsInsideRange(
                                            location.StartLine,
                                            issueLocation,
                                            lineOffset);
                                        var otherEndLineInsideRange = LocationHelper.IsInsideRange(
                                            location.EndLine,
                                            issueLocation,
                                            lineOffset);

                                        return (startLineInsideRange || endLineInsideRange)
                                            || (otherStartLineInsideRange || otherEndLineInsideRange);
                                    }));
                            return overlap;
                        }

                        return locations.Count == queryFiles.Count
                            && locations.All(
                                location => queryFiles.Any(
                                    issueLocation =>
                                        issueLocation.FileName == location.FileName
                                        && issueLocation.StartLine == location.StartLine
                                        && issueLocation.EndLine == location.EndLine));
                    })
                .ToList();
        }

        public enum DetectionMethod
        {
            Overlap,
            Exact
        }
    }
}