using System;
using System.Text;

namespace CcWorks
{
    public class Parameters
    {
        private readonly string[] _args;
        private int _current;

        public Parameters(string[] args)
        {
            _args = args;
            _current = 0;
        }

        public string Get(string prompt)
        {
            if (Any())
            {
                if (!_args[_current].StartsWith("\""))
                {
                    return _args[_current++];
                }

                var builder = new StringBuilder();
                while (Any())
                {
                    var s = _args[_current];
                    var isLast = s.EndsWith("\"") && !s.EndsWith("\\\"");

                    builder.Append(s);
                    _current++;

                    if (isLast)
                    {
                        break;
                    }
                    else
                    {
                        builder.Append(" ");
                    }
                }

                return builder.ToString().Substring(1, builder.Length - 2);
            }
            else
            {
                Console.Write(prompt);
                return (Console.ReadLine() ?? string.Empty).Trim();
            }
        }

        public string GetJiraTicket()
        {
            return Get("Enter jira ticket: ");
        }

        public string GetPrUrl()
        {
            return Get("Enter PR url: ");
        }

        public bool Any()
        {
            return _current < _args.Length;
        }
    }
}