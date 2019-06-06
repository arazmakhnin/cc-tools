using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CcWorks.Exceptions;

namespace CcWorks.Helpers
{
    public static class GitHelper
    {
        public static IReadOnlyCollection<string> Exec(string command, string repoName, string projectsPath)
        {
            var projectPath = Path.Combine(projectsPath, repoName);
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
    }
}