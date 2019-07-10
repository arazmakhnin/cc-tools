using System;
using System.IO;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Exceptions;
using CcWorks.Helpers;
using CcWorks.Workers.Solvers;

namespace CcWorks.Workers
{
    public static class SolveWorker
    {
        public static async Task DoWork(CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            var shortTicketType = parameters.Get("Enter ticket type: ");
            if (shortTicketType.ToLowerInvariant() != "brp")
            {
                throw new CcException("Unsupported ticket type: " + shortTicketType);
            }

            var brpQualifier = parameters.Get("Please specify BRP type [m = Magic Strings, f = Formatting]: ");
            if (brpQualifier.ToLowerInvariant() != "m")
            {
                throw new CcException("Unsupported BRP type: " + brpQualifier);
            }
            
            var fileName = parameters.Get("Enter full file name: ");
            var fileText = File.ReadAllText(fileName);

            Console.Write("Solving... ");
            var result = await BrpMagicStringsSolver.Solve(fileText);

            if (result.Stats.ConstantsCreated != 0 || result.Stats.EmptyStringsReplaced != 0)
            {
                File.WriteAllText(fileName, result.FileText);
                Console.WriteLine("done");

                Console.WriteLine($"Empty strings replaced: {result.Stats.EmptyStringsReplaced}");
                Console.WriteLine($"Constants created: {result.Stats.ConstantsCreated}");
                Console.WriteLine($"Magic strings replaced: {result.Stats.MagicStringsReplaced}");
            }
            else
            {
                ConsoleHelper.WriteLineColor("nothing changed", ConsoleColor.Yellow);
            }
        }
    }
}