using System.Collections.Generic;

namespace CcWorks
{
    public class Settings
    {
        public CommonSettings CommonSettings { get; set; }
        public FpCommandSettings FpCommand { get; set; }
        public PrCommandSettings PrCommand { get; set; }
        public NewCommandSettings NewCommand { get; set; }
        public CrnCommandSettings CrnCommand { get; set; }
        public CiCommandSettings CiCommand { get; set; }
        public ReviewCommandSettings ReviewCommand { get; set; }
        public RebaseCommandSettings RebaseCommand { get; set; }
        public DuplicateTicketCommandSettings DuplicateTicketCommand { get; set; }
    }

    public class CommonSettings
    {
        public string JiraUserName { get; set; }
        public string JiraPassword { get; set; }
        public string GithubToken { get; set; }
        public string ProjectsPath { get; set; }
        public RepoSettings[] Repos { get; set; }
    }

    public class RepoSettings
    {
        public string Name { get; set; }
        public string Pca { get; set; }
        public string BranchPrefix { get; set; }
        public string MainBranch { get; set; }
    }

    public class FpCommandSettings
    {
        
    }

    public class PrCommandSettings
    {
        public bool AssignPr { get; set; }
        public Dictionary<string, string> CommentAliases { get; set; }
        public Dictionary<string, string> NotesAliases { get; set; }
    }

    public class NewCommandSettings
    {
        public string[] FieldsToCopy { get; set; }
    }

    public class CrnCommandSettings
    {

    }

    public class CiCommandSettings
    {

    }

    public class ReviewCommandSettings
    {
        public bool AssignPr { get; set; }
    }

    public class RebaseCommandSettings
    {
    }

    public class DuplicateTicketCommandSettings
    {
        public bool? CheckSameTypeOnly { get; set; }
        public bool? CheckDuplicateForNew { get; set; }
        public int LineOffset { get; set; } = 0;
    }
}