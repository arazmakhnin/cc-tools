using System;
using System.Linq;

namespace CcWorks.Helpers
{
    public static class SettingsHelper
    {
        public static RepoSettings GetRepoSettings(CommonSettings commonSettings, string repoName)
        {
            return commonSettings.Repos.SingleOrDefault(r => r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        }
    }
}