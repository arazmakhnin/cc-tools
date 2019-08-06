using System.Collections.Generic;
using System.Text.RegularExpressions;
using Atlassian.Jira;
using Newtonsoft.Json;

namespace CcWorks.Helpers
{
    public static class LocationHelper
    {
        public static bool IsInsideRange(int position, IssueLocation location, int lineOffset)
        {
            return (position >= location.StartLine + lineOffset
                    || position >= location.StartLine - lineOffset)
                && (position <= location.EndLine + lineOffset
                    || position <= location.EndLine - lineOffset);
        }

        public static List<IssueLocation> GetIssueLocationsFromTicket(Issue issue)
        {
            return GetIssueLocationsFromDescription(issue.Description);
        }

        public static List<IssueLocation> GetIssueLocationsFromDescription(string issueDescription)
        {
            var fileRegex = new Regex(@"\[([^\]]+)\]");
            var json = fileRegex.Match(issueDescription).Value;
            return JsonConvert.DeserializeObject<List<IssueLocation>>(json);
        }
    }
}