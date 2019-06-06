using System;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using Newtonsoft.Json.Linq;

namespace CcWorks.Workers
{
    public static class CrnWorker
    {
        public static async Task DoWork(CrnCommandSettings settings, CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            throw new CcException("Crn command isn't implemented yet");

            var prUrl = parameters.GetPrUrl();
            GithubHelper.ParsePrUrl(prUrl, out var repoName, out var prNumber);

            Console.Write("Getting PR... ");
            var query = @"query {
                repository(owner:""trilogy-group"", name:""" + repoName + @"""){
                    pullRequest(number: " + prNumber + @"){
                        headRefName
                    }
                }  
            }";

            var repoData = await GithubHelper.Query(query, commonSettings.GithubToken);
            var branchName = repoData["repository"]["pullRequest"]["headRefName"].Value<string>();

            Console.WriteLine("done");

            Console.Write($"Checkout branch {branchName}... ");
            GitHelper.Exec($"git checkout {branchName}", repoName, commonSettings.ProjectsPath);
            Console.WriteLine("done");
        }
    }
}