using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using CcWorks.Workers;
using Newtonsoft.Json;

namespace CcWorks
{
    class Program
    {
        // Last changes:
        // 1. "Pr": optional key "--onlyPr" added
        static async Task Main(string[] args)
        {
            var onlyOne = args.Any();

            var settings = ReadSettings();
            var jira = Jira.CreateRestClient(
                "https://jira.devfactory.com", 
                settings.CommonSettings.JiraUserName, 
                settings.CommonSettings.JiraPassword);

            var parameters = args;
            while (true)
            {
                if (!onlyOne)
                {
                    Console.Write("Enter command: ");
                    parameters = (Console.ReadLine() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (!parameters.Any())
                    {
                        continue;
                    }
                }

                var command = parameters[0];
                var commandParameters = new Parameters(parameters.Skip(1).ToArray());

                try
                {
                    switch (command.ToLower())
                    {
                        case "fp":
                            await FalsePositiveWorker.DoWork(settings.FpCommand, commandParameters, jira);
                            break;

                        case "new":
                            await NewWorker.DoWork(settings.NewCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "pr":
                            await MakePrWorker.DoWork(settings.PrCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "crn":
                            await CrnWorker.DoWork(settings.CrnCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "ci":
                            await CiWorker.DoWork(settings.CrnCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "review":
                            await ReviewWorker.DoWork(settings.ReviewCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "rebase":
                            await RebaseWorker.DoWork(settings.RebaseCommand, settings.CommonSettings, commandParameters, jira);
                            break;

                        case "solve":
                            await SolveWorker.DoWork(settings.CommonSettings, commandParameters, jira);
                            break;

                        case "exit":
                            return;

                        default:
                            Console.WriteLine($"Unknown command: {command}");
                            break;
                    }
                }
                catch (CcException ex)
                {
                    if (Console.CursorLeft > 0)
                    {
                        Console.WriteLine();
                    }

                    ConsoleHelper.WriteLineColor(ex.Message, ConsoleColor.Red);
                }
                catch (GitException ex)
                {
                    Console.WriteLine();
                    Console.Write("Git process exited with error code");
                    Console.WriteLine();

                    foreach (var line in ex.Output)
                    {
                        Console.WriteLine(line);
                    }

                    foreach (var line in ex.Error)
                    {
                        ConsoleHelper.WriteLineColor(line, ConsoleColor.Red);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine();
                    Console.WriteLine(e);

                    File.AppendAllText(AppFolderHelper.GetFile("log.txt"),$"[{DateTime.Now}] {e}\r\n");
                }

                if (onlyOne)
                {
                    break;
                }

                Console.WriteLine("================================");
            }
        }

        private static Settings ReadSettings()
        {
            var text = File.ReadAllText(AppFolderHelper.GetFile("settings.json"));
            var settings = JsonConvert.DeserializeObject<Settings>(text);

            var common = settings.CommonSettings;
            if (string.IsNullOrWhiteSpace(common.JiraUserName) || string.IsNullOrWhiteSpace(common.JiraPassword))
            {
                throw new InvalidOperationException("Jira credentials are incorrect");
            }

            if (string.IsNullOrWhiteSpace(common.GithubToken))
            {
                throw new InvalidOperationException("Github token is incorrect");
            }

            if (!Directory.Exists(common.ProjectsPath))
            {
                throw new InvalidOperationException($"Path \"{common.ProjectsPath}\" doesn't exist");
            }

            return settings;
        }
    }
}
