using System;

namespace CcWorks.Exceptions
{
    public class CcException : Exception
    {
        public CcException(string message) : base(message)
        {
            
        }
    }
}