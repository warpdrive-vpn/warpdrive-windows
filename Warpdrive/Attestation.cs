using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Warpdrive
{
    public static class Identifier
    {
        static Logger Log = LogManager.GetCurrentClassLogger();
        public static byte[] Value;
        public static void Create()
        {
            Value = GetIdentifier();
            Log.Trace("Identifier is now {0}", BitConverter.ToString(Value).Replace("-", "").ToLower());
        }

        public static byte[] GetIdentifier()
        {
            var sha = new SHA1CryptoServiceProvider();
            string id_all = Environment.MachineName; // temporary measure until we can build a better attestation measure

            return sha.ComputeHash(Encoding.UTF8.GetBytes(id_all)).Take(8).ToArray();
        }
    }
}
