using System;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Jira;

// ReSharper disable ComplexConditionExpression
namespace CcWorks.Workers
{
    public static class BacklogCleanupWorker
    {
        public static async Task DoWork(BacklogCleanupCommandSettings settings, Parameters parameters, Jira jira)
        {
            Console.WriteLine("This operation cleans up the backlog within a few steps.");
            var totalIssues = GetTotalTicketsToCheck(settings, parameters);
            var pastDaysForDone = PastDaysForDoneTickets(settings, parameters);
            totalIssues = await AnalyzeDoneTickets(jira, totalIssues, pastDaysForDone);
            var pastDaysForCreated = PastDaysForCreatedTickets(settings, parameters);
            totalIssues = await AnalyzeCreatedTickets(jira, totalIssues, pastDaysForCreated);
            // TODO: Check tickets stuck InProgress, InReview, InMerge, CheckedIn and notify somehow
            // TODO: Starting with HUT done tickets, check for coverage on tickets that are on the same file
            await AnalyzeAllTickets(jira, totalIssues);
        }

        private static int PastDaysForDoneTickets(BacklogCleanupCommandSettings settings, Parameters parameters)
        {
            int pastDaysForDoneTickets;

            if (settings.PastDaysForDoneTickets.HasValue)
            {
                pastDaysForDoneTickets = settings.PastDaysForDoneTickets.Value;
                Console.WriteLine(
                    $"We will start by analyzing tickets moved to done in the last : {settings.PastDaysForDoneTickets.Value} days, this amount can be changed in the settings file");
            }
            else
            {
                var ticketsNo = parameters.Get(
                    "Please provide how many days in the past you want to process for Done tickets");
                pastDaysForDoneTickets = int.Parse(ticketsNo);
            }

            return pastDaysForDoneTickets;
        }

        private static int PastDaysForCreatedTickets(BacklogCleanupCommandSettings settings, Parameters parameters)
        {
            int pastDaysForDoneTickets;

            if (settings.PastDaysForCreatedTickets.HasValue)
            {
                pastDaysForDoneTickets = settings.PastDaysForCreatedTickets.Value;
                Console.WriteLine(
                    $"We will start by analyzing tickets created in the last : {settings.PastDaysForCreatedTickets.Value} days, this amount can be changed in the settings file");
            }
            else
            {
                var ticketsNo = parameters.Get(
                    "Please provide how many days in the past you want to process for Done tickets");
                pastDaysForDoneTickets = int.Parse(ticketsNo);
            }

            return pastDaysForDoneTickets;
        }

        private static int GetTotalTicketsToCheck(BacklogCleanupCommandSettings settings, Parameters parameters)
        {
            int totalIssues;

            if (settings.MaxIssuesPerRun.HasValue)
            {
                totalIssues = settings.MaxIssuesPerRun.Value;
                Console.WriteLine(
                    $"In total we will process: {settings.MaxIssuesPerRun.Value} DuplicateTicketCommandSettings, this amount can be changed in the settings file");
            }
            else
            {
                var ticketsNo = parameters.Get(
                    "Please provide how many tickets you want to process in total for this operation");
                totalIssues = int.Parse(ticketsNo);
            }

            return totalIssues;
        }

        private static async Task<int> AnalyzeDoneTickets(Jira jira, int totalIssues, int pastDaysForDone)
        {
            var pastDayQuery = $@"AND status changed (""Done"") after startOfDay(-{pastDaysForDone}d)";
            var statuses = @"AND status in (""done"")";
            var usefulTickets =
                @"AND assignee is not EMPTY AND type not in (Story, epic, ""Hand Crafted Unit Tests"", ""Bulk Unit Tests"", ""Product Commit QE Review"", review) ";
            //TODO: add to query below a check to ignore this label if applied after the value defined in ExpirationDaysForProcessedTickets
            var labels = $@"and (labels not in(""{TicketLabels.ProcessedByCcTool}"") or labels is EMPTY)";

            var jql = $@"Project = CC {usefulTickets} {statuses} {pastDayQuery} {labels} Order By Updated DESC";

            var processedItems = await ForEachIssue(
                jira,
                jql,
                totalIssues,
                issue =>
                {
                    // TODO: Add all tickets to be modified into a collection and modify them at once
                    DetectDuplicatedTicket(jira, issue);
                    InvalidateOverlappedTicket(jira, issue);
                    AddProcessedLabel(jira, issue);
                });

            return totalIssues - processedItems;
        }

        private static void AddProcessedLabel(Jira jira, Issue issue)
        {
            issue.Labels.Add(TicketLabels.ProcessedByCcTool.ToString());
            jira.Issues.UpdateIssueAsync(issue);
        }

        private static async Task InvalidateOverlappedTicket(Jira jira, Issue issue)
        {
            var overlapTickets = DuplicateTicketChecker.GetDuplicatedTickets(issue.Key.Value, true, jira, 20).Result;

            overlapTickets.ForEach(
                dup =>
                {
                    dup.Labels.Add(TicketLabels.OverlapFoundWillBeInvalidated.ToString());
                    jira.Issues.UpdateIssueAsync(dup);
                });
        }

        private static async Task<int> AnalyzeCreatedTickets(Jira jira, int totalIssues, int pastDaysForCreated)
        {
            var pastDayQuery = $@"AND createdDate > startOfDay(-{pastDaysForCreated}d)";
            var usefulTickets =
                @"AND assignee is not EMPTY AND type not in (Story, epic, ""Hand Crafted Unit Tests"", ""Bulk Unit Tests"", ""Product Commit QE Review"", review) ";

            //TODO: add to query below a check to ignore this label if applied after the value defined in ExpirationDaysForProcessedTickets
            var labels = $@"and (labels not in(""{TicketLabels.ProcessedByCcTool}"") or labels is EMPTY)";

            var jql = $@"Project = CC {usefulTickets} {pastDayQuery} {labels} Order By Updated DESC";

            var processedItems = await ForEachIssue(
                jira,
                jql,
                totalIssues,
                issue =>
                {
                    // TODO: Add all tickets to be modified into a collection and modify them at once
                    DetectDuplicatedTicket(jira, issue);
                    //TODO: Check for coverage
                    AddProcessedLabel(jira, issue);
                });

            return totalIssues - processedItems;
        }

        private static Task AnalyzeAllTickets(Jira jira, int totalIssues)
        {
            Console.WriteLine($"Smart analysis completed, now we will check all open tickets, for the remaining of {totalIssues} to not take too much time"); 

            var usefulTickets =
                @"AND assignee is not EMPTY AND type not in (Story, epic, ""Hand Crafted Unit Tests"", ""Bulk Unit Tests"", ""Product Commit QE Review"", review) ";

            var statuses =
                @"AND status in (""to do"", ""in progress"", ""ready to refactor"", ""ready for review"", ""code review"", ""code merge"", ""code checked in"")";

            //TODO: add to query below a check to ignore this label if applied after the value defined in ExpirationDaysForProcessedTickets
            var labels = $@"and (labels not in(""{TicketLabels.ProcessedByCcTool}"") or labels is EMPTY)";

            var jql = $@"Project = CC {usefulTickets} {statuses} {labels} Order By Updated DESC";

            return ForEachIssue(
                jira,
                jql,
                totalIssues,
                issue =>
                {
                    // TODO: Add all tickets to be modified into a collection and modify them at once
                    DetectDuplicatedTicket(jira, issue);
                    // TODO: Fill missing data (EffectedLoC, PCA reviewer)
                    // TODO: Check for coverage
                    AddProcessedLabel(jira, issue);
                });
        }

        private static async Task DetectDuplicatedTicket(Jira jira, Issue issue)
        {
            var duplicatedTickets = DuplicateTicketChecker.GetDuplicatedTickets(
                    issue.Key.Value,
                    true,
                    jira,
                    0,
                    DuplicateTicketChecker.DetectionMethod.Exact)
                .Result;

            if (duplicatedTickets.Count > 0)
            {
                issue.Labels.Add(TicketLabels.DuplicateFoundWillBeKept.ToString());
            }

            duplicatedTickets.ForEach(
                dup =>
                {
                    dup.Labels.Add(TicketLabels.DuplicateFoundWillBeDeleted.ToString());
                    jira.Issues.UpdateIssueAsync(dup);
                });
        }

        // Helper method that allows us to iterate over the entire collection as if it was a single result
        // While internally it handles the pagination from Jira
        // TODO check how to await when calling the action, likely will have to extract as an async method instead of using lambda to call
        private static async Task<int> ForEachIssue(Jira jira, string query, int totalIssues, Action<Issue> action)
        {
            var itemsPerPage = 200;
            var startAt = 0;
            var processedIssues = 0;

            while (true)
            {
                if (processedIssues >= totalIssues)
                {
                    break;
                }

                var result = await jira.Issues.GetIssuesFromJqlAsync(query, itemsPerPage, startAt);

                if (!result.Any())
                {
                    break;
                }

                foreach (var issue in result)
                {
                    totalIssues++;
                    action(issue);
                }

                startAt += itemsPerPage;
            }

            return processedIssues;
        }

        private enum TicketLabels
        {
            ProcessedByCcTool,
            DuplicateFoundWillBeDeleted,
            OverlapFoundWillBeInvalidated,
            DuplicateFoundWillBeKept,
            CoverageCheckByCcToolNoCoverageInfo,
            CoverageCheckByCcToolCovered,
            CoverageCheckByCcToolNotCovered
        }
    }
}