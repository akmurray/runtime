using System.Diagnostics;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text.Json;

const int RequestBytes = 853, ReplyBytes = 977;
byte[] reply = new byte[ReplyBytes];
Array.Fill(reply, (byte)0xab);
if (args.Length == 1 && args[0] == "server")
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.Logging.ClearProviders();
    var app = builder.Build();
    app.UseWebSockets();
    app.MapGet("/json", () => Results.Json(new { message = "Hello, World!" }));
    async Task Echo(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var request = new byte[RequestBytes];
        while (socket.State == WebSocketState.Open)
        {
            int count = 0;
            ValueWebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(request.AsMemory(count), context.RequestAborted);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return;
                }
                if (frame.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Expected a binary request");
                count += frame.Count;
                if (!frame.EndOfMessage && count == request.Length)
                    throw new InvalidDataException("Request too large");
            } while (!frame.EndOfMessage);
            if (count != RequestBytes) throw new InvalidDataException("Unexpected request size");
            if (context.Request.Path == "/dispatch")
            {
                await Task.Run(async () =>
                    await socket.SendAsync(reply.AsMemory(), WebSocketMessageType.Binary, true, context.RequestAborted));
            }
            else
            {
                await socket.SendAsync(reply.AsMemory(), WebSocketMessageType.Binary, true, context.RequestAborted);
            }
        }
    }
    app.Map("/ws", Echo);
    app.Map("/dispatch", Echo);
    await app.RunAsync("http://0.0.0.0:5080");
    return;
}

if (args.Length != 5 || args[0] != "load")
    throw new ArgumentException("server | load URI connections warmupSeconds measureSeconds");
var url = new Uri(args[1]);
int n = int.Parse(args[2]), warm = int.Parse(args[3]), seconds = int.Parse(args[4]);
using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
int phase = 0;
int ready = 0;
var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
async Task<(long count, List<long> samples)> Pump()
{
    using var ws = new ClientWebSocket();
    var tx = new byte[RequestBytes];
    var buffer = new byte[ReplyBytes];
    bool useWs = url.Scheme == "ws";
    if (useWs) await ws.ConnectAsync(url, CancellationToken.None);
    long count = 0;
    long exchanges = 0;
    var samples = new List<long>();
    bool primed = false;
    while (Volatile.Read(ref phase) != 2)
    {
        long begin = (count & 63) == 0 ? Stopwatch.GetTimestamp() : 0;
        if (useWs)
        {
            await ws.SendAsync(tx.AsMemory(), WebSocketMessageType.Binary, true, CancellationToken.None);
            int used = 0;
            ValueWebSocketReceiveResult frame;
            do
            {
                frame = await ws.ReceiveAsync(buffer.AsMemory(used), CancellationToken.None);
                if (frame.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException();
                used += frame.Count;
                if (!frame.EndOfMessage && used == buffer.Length) throw new InvalidDataException();
            } while (!frame.EndOfMessage);
            if (used != ReplyBytes) throw new InvalidDataException();
            if ((exchanges & 63) == 0 && !buffer.AsSpan().SequenceEqual(reply))
                throw new InvalidDataException("Unexpected WebSocket reply");
        }
        else
        {
            using var result = await http.GetAsync(url);
            result.EnsureSuccessStatusCode();
            if ((exchanges & 63) == 0)
            {
                byte[] json = await result.Content.ReadAsByteArrayAsync();
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.GetProperty("message").GetString() != "Hello, World!")
                    throw new InvalidDataException("Unexpected JSON reply");
            }
        }
        exchanges++;
        if (!primed)
        {
            primed = true;
            if (Interlocked.Increment(ref ready) == n) allReady.SetResult();
        }
        if (Volatile.Read(ref phase) == 1)
        {
            count++;
            if (begin != 0) samples.Add(Stopwatch.GetTimestamp() - begin);
        }
    }
    if (useWs) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    return (count, samples);
}
var tasks = Enumerable.Range(0,n).Select(_ => Pump()).ToArray();
Task finished = Task.WhenAll(tasks);
await Task.WhenAny(allReady.Task, finished).WaitAsync(TimeSpan.FromSeconds(30));
if (finished.IsCompleted) await finished;
await Task.Delay(TimeSpan.FromSeconds(warm));
static (long cpu, long bytes) Counters()
{
    string? path = Environment.GetEnvironmentVariable("BENCH_PROCESS_SOCKET");
    if (path == null) return (0, 0);
    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Connect(new UnixDomainSocketEndPoint(path));
    using var stream = new NetworkStream(socket);
    Span<byte> pair = stackalloc byte[16];
    stream.ReadExactly(pair);
    return (BinaryPrimitives.ReadInt64LittleEndian(pair), BinaryPrimitives.ReadInt64LittleEndian(pair[8..]));
}
var first = Counters();
long start = Stopwatch.GetTimestamp();
Volatile.Write(ref phase, 1);
await Task.Delay(TimeSpan.FromSeconds(seconds));
Volatile.Write(ref phase, 2);
double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
var last = Counters();
var results = await Task.WhenAll(tasks);
long completed = results.Sum(result => result.count);
var latencies = results.SelectMany(result => result.samples).Order().ToArray();
double p99Ms = 1000d * latencies[(int)(.99 * (latencies.Length - 1))] / Stopwatch.Frequency;
double? cpuUs = last.cpu == 0 ? null : (last.cpu - first.cpu) / (10d * completed);
double? allocB = last.bytes == 0 ? null : (last.bytes - first.bytes) / (double)completed;
Console.WriteLine(JsonSerializer.Serialize(new { connections=n, completed, elapsed, rps=completed/elapsed, p99Ms, cpuUs, allocB }));
