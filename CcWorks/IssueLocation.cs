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
    }
}