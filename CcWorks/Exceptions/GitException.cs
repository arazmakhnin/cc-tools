using System;
using System.Collections.Generic;

namespace CcWorks.Exceptions
{
    public class GitException : Exception
    {
        public List<string> Output { get; }
        public List<string> Error { get; }

        public GitException(List<string> output, List<string> error)
        {
            Output = output;
            Error = error;
        }
    }
}