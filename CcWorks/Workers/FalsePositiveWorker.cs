﻿using System;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Helpers;
using JiraHelper = CcWorks.Helpers.JiraHelper;

namespace CcWorks.Workers
{
    public static class FalsePositiveWorker
    {
        public static async Task DoWork(FpCommandSettings settings, Parameters parameters, Jira jira)
        {
            var issueKey = JiraHelper.GetIssueKey(parameters.Get("Enter Jira ticket: "));
            var newLabel = parameters.Get("Label (1 - AlreadySolved, 2 - DuplicatedTicket, 3 - IndependentModules, 4 - IncorrectFinding, 5 - UnsolvableWithinCCProcess, 6 - AutoGeneratedCode): ");

            var duplicates = string.Empty;
            switch (newLabel)
            {
                case "1":
                    newLabel = "AlreadySolved";
                    break;

                case "2":
                    newLabel = "DuplicatedTicket";
                    duplicates = parameters.Get("Enter duplicated ticket: ");
                    JiraHelper.CheckUrl(duplicates);
                    break;

                case "3":
                    newLabel = "IndependentModules";
                    break;

                case "4":
                    newLabel = "IncorrectFinding";
                    break;

                case "5":
                    newLabel = "UnsolvableWithinCCProcess";
                    break;

                case "6":
                    newLabel = "AutoGeneratedCode";
                    break;
            }

            Console.Write("Get ticket... ");
            var issue = await jira.Issues.GetIssueAsync(issueKey);
            Console.WriteLine("done");

            Console.Write("Move to FP... ");
            if (issue.Status.ToString().Equals("In progress", StringComparison.InvariantCultureIgnoreCase))
            {
                await issue.WorkflowTransitionAsync("To Do");
            }
            else if (issue.Status.ToString().Equals("Ready to refactor", StringComparison.InvariantCultureIgnoreCase))
            {
                await issue.WorkflowTransitionAsync("To Do");
            }
            else if (!issue.Status.ToString().Equals("To Do", StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine($"Unknown status: {issue.Status}");
                return;
            }

            await issue.WorkflowTransitionAsync("False Positive");
            Console.WriteLine("done");

            Console.Write("Add label... ");
            issue.Labels.Add(newLabel);
            await issue.SaveChangesAsync();
            Console.WriteLine("done");

            if (newLabel == "DuplicatedTicket")
            {
                Console.Write("Leave comment... ");
                await issue.AddCommentAsync($"Duplicates {duplicates}");
                Console.WriteLine("done");
            }
        }
    }
}