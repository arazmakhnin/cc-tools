using System.IO;
using System.Threading.Tasks;
using Atlassian.Jira;
using CcWorks.Workers.Solvers;

namespace CcWorks.Workers
{
    public static class SolveWorker
    {
        public static async Task DoWork(CommonSettings commonSettings, Parameters parameters, Jira jira)
        {
            var fileName = parameters.Get("File name: ");

            var fileText = File.ReadAllText(fileName);

            var newFileText = await BrpMagicStringsSolver.Solve(fileText);

            File.WriteAllText("c:\\projects\\CcWorks\\2.cs", newFileText);
        }
    }
}