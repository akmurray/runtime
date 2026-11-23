using System.Diagnostics;
using System.Text.Json;

int fanout = int.Parse(args[0]), competitors = int.Parse(args[1]);
var batch = new Batch();
var work = Enumerable.Range(0, fanout).Select(_ => new Item(batch)).ToArray();
using var stop = new CancellationTokenSource();
var busy = Enumerable.Range(0, competitors).Select(_ => new Thread(() =>
{
    while (!stop.IsCancellationRequested) Thread.SpinWait(128);
}) { IsBackground = true }).ToArray();
foreach (var thread in busy) thread.Start();

void RunBatch(bool timed)
{
    batch.Expected += fanout;
    Volatile.Write(ref batch.Remaining, fanout);
    foreach (var item in work)
    {
        item.Start = timed ? Stopwatch.GetTimestamp() : 0;
        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
    }
    if (!batch.Done.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
}
var watch = Stopwatch.StartNew();
while (watch.Elapsed.TotalSeconds < 3) RunBatch(false);
var latencies = new List<long>();
using var process = Process.GetCurrentProcess();
var cpu = process.TotalProcessorTime;
int batches = 0;
watch.Restart();
while (watch.Elapsed.TotalSeconds < 6)
{
    bool timed = (batches++ & 15) == 0;
    RunBatch(timed);
    if (timed) foreach (var item in work) latencies.Add(item.Latency);
}
double elapsed = watch.Elapsed.TotalSeconds;
double cpuUs = (process.TotalProcessorTime - cpu).TotalMicroseconds / (batches * (double)fanout);
stop.Cancel();
foreach (var thread in busy) thread.Join();
latencies.Sort();
if (batch.Completed != (long)batch.Expected) throw new InvalidDataException("Incomplete work");
Console.WriteLine(JsonSerializer.Serialize(new {
    fanout, competitors, batches, elapsed, rps=batches*(double)fanout/elapsed, cpuUs, samples=latencies.Count,
    p50Us=latencies[latencies.Count/2]*1e6/Stopwatch.Frequency, p99Us=latencies[(int)(.99*(latencies.Count-1))]*1e6/Stopwatch.Frequency }));

sealed class Batch
{
    public readonly AutoResetEvent Done = new(false);
    public int Remaining;
    public long Completed, Expected;
}
sealed class Item(Batch batch) : IThreadPoolWorkItem
{
    public long Start, Latency;
    public void Execute()
    {
        if (Start != 0) Latency = Stopwatch.GetTimestamp() - Start;
        Interlocked.Increment(ref batch.Completed);
        if (Interlocked.Decrement(ref batch.Remaining) == 0) batch.Done.Set();
    }
}
