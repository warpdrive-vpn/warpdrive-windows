using Bifrost;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Warpdrive
{
    public class TunInterface
    {
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateFile(
            string filename,
            [MarshalAs(UnmanagedType.U4)]FileAccess fileaccess,
            [MarshalAs(UnmanagedType.U4)]FileShare fileshare,
            int securityattributes,
            [MarshalAs(UnmanagedType.U4)]FileMode creationdisposition,
            int flags,
            IntPtr template);
        const int FILE_ATTRIBUTE_SYSTEM = 0x4;
        const int FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;
        private const uint FILE_DEVICE_UNKNOWN = 0x00000022;

        static EventWaitHandle ReadWait = new EventWaitHandle(false, EventResetMode.AutoReset);
        static EventWaitHandle WriteWait = new EventWaitHandle(false, EventResetMode.AutoReset);

        static string UsermodeDeviceSpace = "\\\\.\\Global\\";
        static string AdapterKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}";

        public FileStream Stream;
        public string Name;

        public bool FakeRead = false;
        public bool FakeWrite = false;

        public long BytesReceived = 0;
        public long BytesSent = 0;

        public bool SpoofedPings = false;

        private byte[] _ip;
        private byte[] _subnet;

        private TunInterface(FileStream stream)
        {
            Stream = stream;
        }

        public static TunInterface Open(string name, byte[] ip, byte[] subnet)
        {
            int len = 0;

            string guid = GetDeviceGuid();

            IntPtr pstatus = Marshal.AllocHGlobal(4);
            IntPtr ptun = Marshal.AllocHGlobal(12);
            IntPtr ptr = CreateFile(UsermodeDeviceSpace + guid + ".tap", FileAccess.ReadWrite, FileShare.ReadWrite, 0, FileMode.Open, FILE_ATTRIBUTE_SYSTEM | FILE_FLAG_OVERLAPPED, IntPtr.Zero);

            Marshal.WriteInt32(pstatus, 1);
            DeviceIoControl(ptr, TAP_CONTROL_CODE(6, METHOD_BUFFERED), pstatus, 4, pstatus, 4, out len, IntPtr.Zero);

            int ip_int = BitConverter.ToInt32(ip, 0);
            int mask_int = BitConverter.ToInt32(subnet, 0);

            Marshal.WriteInt32(ptun, 0, ip_int);
            Marshal.WriteInt32(ptun, 4, ip_int & mask_int);
            Marshal.WriteInt32(ptun, 8, mask_int);
            DeviceIoControl(ptr, TAP_CONTROL_CODE(10, METHOD_BUFFERED), ptun, 12, ptun, 12, out len, IntPtr.Zero);

            FileStream stream = new FileStream(new SafeFileHandle(ptr, true), FileAccess.ReadWrite, 1, true);

            return new TunInterface(stream)
            {
                Name = name,
                _ip = ip,
                _subnet = subnet
            };
        }

        public TunInterface Reopen()
        {
            return Open(Name, _ip, _subnet);
        }

        private static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
        {
            return ((DeviceType << 16) | (Access << 14) | (Function << 2) | Method);
        }

        static uint TAP_CONTROL_CODE(uint request, uint method)
        {
            return CTL_CODE(FILE_DEVICE_UNKNOWN, request, method, FILE_ANY_ACCESS);
        }

        static string GetDeviceGuid()
        {
            RegistryKey adapters = Registry.LocalMachine.OpenSubKey(AdapterKey);
            string[] keys = adapters.GetSubKeyNames();

            foreach (string x in keys)
            {
                try
                {
                    RegistryKey adapter = adapters.OpenSubKey(x);
                    object id = adapter.GetValue("ComponentId");
                    if (id != null && id.ToString() == "tap0901")
                        return adapter.GetValue("NetCfgInstanceId").ToString();
                }
                catch
                {

                }
            }
            return "";
        }

        public byte[] Read()
        {
            if (FakeRead)
                return new byte[1500];

            byte[] buf = new byte[1500];
            int read = 0;
            
            read = Stream.Read(buf, 0, buf.Length);
            BytesReceived += read;

            byte[] data = Utilities.ShrinkArray(buf, read);

            if (SpoofedPings && NetworkTools.IsICMPPacket(data))
            {
                var swapped = NetworkTools.SwapDestinationSource(data);
                var patched = NetworkTools.MakeICMPResponse(swapped);

                Write(patched);

                return new byte[0];
            }

            return data;
        }

        public void Close()
        {
            Stream.Close();
        }

        public int Write(byte[] data)
        {
            if (FakeWrite)
                return data.Length;

            lock (Stream)
            {
                // TODO: we might be better off using some other form of sync here
                Stream.Write(data, 0, data.Length);
            }
            
            BytesSent += data.Length;

            return data.Length;
        }
    }
}

