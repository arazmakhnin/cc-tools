using Atlassian.Jira;

namespace CcWorks.Helpers
{
    public static class IssueHelper
    {
        public static bool OpenStatus(Issue issue)
        {
            var ticketStatus = issue.Status.Name.ToLower();
            return ticketStatus == "to do"
                || ticketStatus == "in progress"
                || ticketStatus == "ready to refactor"
                || ticketStatus == "ready for review"
                || ticketStatus == "code review"
                || ticketStatus == "code merge"
                || ticketStatus == "code checked in";
        }
        public static bool WorkableStatus(Issue issue)
        {
            var ticketStatus = issue.Status;
            return ticketStatus == "Ready To Refactor";
        }

        public static bool ActiveStatus(Issue issue)
        {
            var ticketStatus = issue.Status.Name.ToLower();
            return ticketStatus == "in progress"
                || ticketStatus == "done"
                || ticketStatus == "false positive"
                || ticketStatus == "code merge"
                || ticketStatus == "code checked in";
        }

        public static bool SolvedStatus(Issue issue)
        {
            var ticketStatus = issue.Status.Name.ToLower();
            return ticketStatus == "cancelled"
                || ticketStatus == "ready for review"
                || ticketStatus == "code review"
                || ticketStatus == "code merge"
                || ticketStatus == "code checked in";
        }
    }
}