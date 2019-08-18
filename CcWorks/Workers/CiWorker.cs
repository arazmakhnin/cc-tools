using System;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Helpers;
using Newtonsoft.Json.Linq;

namespace CcWorks.Workers
{
    public class CiWorker
    {
        public static async Task DoWork(CrnCommandSettings settings, CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            var prUrl = parameters.Get("PR url: ");
            GithubHelper.ParsePrUrl(prUrl, out var repoName, out var prNumber);

            var repoSettings = SettingsHelper.GetRepoSettings(commonSettings, repoName);

            Console.Write("Getting PR... ");
            var query = @"query {
                repository(owner:""trilogy-group"", name:""" + repoSettings.Name + @"""){
                    pullRequest(number: " + prNumber + @"){
                        headRefName
                    }
                }  
            }";

            var repoData = await GithubHelper.Query(query, commonSettings.GithubToken);
            var branchName = repoData["repository"]["pullRequest"]["headRefName"].Value<string>();

            Console.WriteLine("done");

            Console.Write($"Push CI trigger to {branchName}... ");
            GitHelper.Exec(
                $"git checkout {branchName} && git commit --allow-empty -m \"Trigger CI Job\" && git push", 
                repoSettings, 
                commonSettings.ProjectsPath);
            Console.WriteLine("done");

            var mainBranch = SettingsHelper.GetMainBranch(repoSettings);
            Console.Write($"Checkout {mainBranch}... ");
            GitHelper.Exec($"git checkout {mainBranch}", repoSettings, commonSettings.ProjectsPath);
            Console.WriteLine("done");
        }
    }
}