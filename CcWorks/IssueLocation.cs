using Atlassian.Jira;
using Newtonsoft.Json;

namespace CcWorks
{
    public class IssueLocation
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("startLine")]
        public int StartLine { get; set; }

        [JsonProperty("endLine")]
        public int EndLine { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is IssueLocation location)
            {
                return FileName == location.FileName && StartLine == location.StartLine && EndLine == location.EndLine;
            }

            return false;
        }
    }
}