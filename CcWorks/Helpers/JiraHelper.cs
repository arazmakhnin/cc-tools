using System;
using System.Linq;
using System.Text.RegularExpressions;
using Atlassian.Jira;
using CcWorks.Exceptions;

namespace CcWorks.Helpers
{
    public class JiraHelper
    {
        private static readonly Regex JiraUrlRegex = new Regex(@"CC-\d+$");

        public static string GetIssueKey(string url)
        {
            var match = JiraUrlRegex.Match(url);
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !match.Success)
            {
                throw new CcException("Unknown jira ticket");
            }

            return match.Value;
        }

        public static void CheckUrl(string url)
        {
            var match = JiraUrlRegex.Match(url);
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !match.Success)
            {
                throw new CcException("Unknown jira ticket");
            }
        }
    }

    public static class JiraIssueHelper
    {
        public static string GetScmUrl(this Issue issue)
        {
            return issue.CustomFields["scm_url"]?.Values?.FirstOrDefault();
        }
    }
}