using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Warpdrive
{
    public static class TimerResolution
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern int NtQueryTimerResolution(out int MinimumResolution, out int MaximumResolution, out int CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern int NtSetTimerResolution(int DesiredResolution, bool SetResolution, out int CurrentResolution);

        public static void TryOptimizeTimerResolution()
        {
            int min = 0;
            int max = 0;
            int current = 0;

            int query_result = NtQueryTimerResolution(out min, out max, out current);

            if (query_result != 0)
            {
                Log.Warn("NtQueryTimerResolution returned 0x{0:X}({0})! Unable to optimize timer resolution(attempt to read current value returned {1}).", query_result, current);
                return;
            }

            Log.Debug("NtQueryTimerResolution: minimum resolution {0}ns, maximum resolution {1}ns, current resolution {2}ns", min, max, current);

            NtSetTimerResolution(max, true, out current);

            int temp = 0;
            NtQueryTimerResolution(out min, out max, out temp);

            if (temp != current)
                Log.Warn("Timer resolution is still {0}ns after calling NtSetTimerResolution!", temp);
            else
                Log.Info("Successfully set timer resolution to {0}ns.", current);
        }
    }
}
