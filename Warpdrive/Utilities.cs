using System;
using System.Text;
using System.IO;
using System.Threading;

namespace Warpdrive
{
    public static class Utilities
    {
        public static byte[] ShrinkArray(byte[] data, int length)
        {
            if (length < 0)
                return null;

            if (length >= data.Length)
                return data;

            byte[] ret = new byte[length];
            Array.Copy(data, ret, length);

            return ret;
        }

        public static void PrintBytes(byte[] data)
        {
            int width = 16;

            MemoryStream ms = new MemoryStream(data);

            for (int i = 0; i < data.Length; i += width)
            {
                byte[] buf = new byte[width];
                int read = ms.Read(buf, 0, width);

                PrintBytesLine(buf, i, read);
            }
        }

        public static void PrintBytesLine(byte[] line, int offset, int length)
        {
            string offset_pretty = offset.ToString("x8");
            StringBuilder bytes = new StringBuilder();

            for(int i = 0; i < length; i++)
                bytes.AppendFormat("{0:x2} ", line[i]);

            for (int i = 0; i < line.Length - length; i++)
                bytes.Append(".. ");

            bytes.Append(" | ");
            char[] chars = Encoding.ASCII.GetChars(line);

            for (int i = 0; i < chars.Length; i++)
                bytes.Append(char.IsControl(chars[i]) ? '.' : chars[i]);

            Console.WriteLine("{0}: {1}", offset_pretty, bytes);
        }

        public static Thread StartThread(Action action)
        {
            Thread thr = new Thread(() => action());
            thr.Start();
            return thr;
        }
    }
}

