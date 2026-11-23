using System.Diagnostics;
using System.Net.Sockets;
using System.Buffers.Binary;

internal static class StartupHook
{
    public static void Initialize()
    {
        string path = Environment.GetEnvironmentVariable("BENCH_PROCESS_SOCKET");
        if (string.IsNullOrEmpty(path)) return;
        File.Delete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(4);
        new Thread(() =>
        {
            using var process = Process.GetCurrentProcess();
            byte[] counters = new byte[16];
            while (true)
            {
                using var connection = listener.Accept();
                BinaryPrimitives.WriteInt64LittleEndian(counters, process.TotalProcessorTime.Ticks);
                BinaryPrimitives.WriteInt64LittleEndian(counters.AsSpan(8), GC.GetTotalAllocatedBytes(precise: true));
                for (int sent = 0; sent < counters.Length;)
                    sent += connection.Send(counters.AsSpan(sent), SocketFlags.None);
            }
        }) { IsBackground = true, Name = "Benchmark counters" }.Start();
    }
}
