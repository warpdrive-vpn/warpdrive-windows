using System;

namespace Warpdrive
{
    public static class Extensions
    {
        public static string[] Split(this string str, params string[] delimiters)
        {
            return str.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

