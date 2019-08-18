using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CcWorks.Exceptions;

namespace CcWorks.Helpers
{
    public static class GitHelper
    {
        public static IReadOnlyCollection<string> Exec(string command, RepoSettings repoSettings, string projectsPath)
        {
            var projectPath = Path.Combine(projectsPath, repoSettings.ActualFolderName);
            if (!Directory.Exists(projectPath))
            {
                throw new CcException($"Path \"{projectPath}\" not found");
            }

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = projectPath,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                RedirectStandardInput = false,
                UseShellExecute = false,
                Arguments = "/C " + command,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var outputList = new List<string>();
            var errorList = new List<string>();
            using (var process = new Process())
            {
                process.StartInfo = startInfo;

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        outputList.Add(args.Data);
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        errorList.Add(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new GitException(outputList, errorList);
                }

                return outputList;
            }
        }

        public static string GetCurrentBranch(RepoSettings repoSettings, string projectsPath)
        {
            var result = Exec("git rev-parse --abbrev-ref HEAD", repoSettings, projectsPath);
            if (!result.Any() || result.Count > 1)
            {
                throw new CcException("Current git branch not found");
            }

            return result.First();
        }
    }
}