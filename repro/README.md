# Compare Linux socket dispatch with Kestrel held fixed

Run on Linux x64 with Bash and Docker. The default placement needs eight logical CPUs visible to Docker. The server uses CPUs 0–3, and the load process uses CPUs 6–7. Both run on one internal bridge network. The driver keeps one request in flight per connection. It treats a failed or malformed WebSocket exchange as a run failure.

```bash
bash run.sh
```

This builds the net10.0 application once. Both stock server processes map ASP.NET Core 10.0.12 from the same image. Each stock runtime uses its own dotnet host. The A/B source comparison holds the host fixed. The load process always uses .NET 10.0.12. Each arm starts a fresh server. The default order is 10, 11, 11, 10. Server GC and thread-pool settings use framework defaults. To reproduce the overrides used in the source comparisons, set `BENCH_PIN_POOL=1`. The driver validates the frame type and length on every WebSocket response and checks the response payload on the first and every 64th exchange of each connection for both protocols.

The three scenarios are an 853-byte binary WebSocket request with a 977-byte response, the same exchange with an explicit task handoff before the send, and an HTTP JSON GET. The workload has no database or external services. Each run prints completed requests, elapsed seconds, requests/s, sampled client p99 in milliseconds, and whole-server CPU microseconds and managed bytes per completion. The client samples one round trip per 64 measured completions per connection. A separate thread in the server reads process counters at the measurement boundaries through a Unix-domain socket.

```bash
SERVER_CPUS=0-3 DRIVER_CPUS=4-7 CONNECTIONS=32 SCENARIOS=ws bash run.sh
```

To compare runtime source builds, mount two complete, Linux x64 .NET 11 shared-framework directories. The script pins the host, driver, application and Kestrel for both arms. The commands below build each side of the same source commit. The CPU-heavy build finishes before starting measurements. Use an empty output path on a Linux filesystem for the build.

```bash
bash build-edge.sh january "$HOME/runtime-jan-socket"
FX_A="$HOME/runtime-jan-socket/A" FX_B="$HOME/runtime-jan-socket/B" \
  ORDER="A B B A" SCENARIOS="ws json" BENCH_PIN_POOL=1 bash run.sh
```

`december` selects the earlier component comparison. January builds CoreLib for both sides and holds the native engine fixed. December also builds CoreCLR, JIT and System.Native for both sides. The commercial workload's authenticated request and reply contain more application work. Its absolute throughput differs from these smaller echo scenarios.

Use `release-fix` in place of `january` to build the same .NET 11.0.0 source twice and apply the tested Linux worker-wait change to B. This version of the method lived in a different source file at GA. The supplied release comparison patch adapts the change to that source layout.

Use `january-wait` for the historical diagnostic. Both sides start from the January commit. B restores the parent's wait loop and its effective budget. The rest of the January commit stays in both builds.

The separate `handoff` executable measures a non-socket producer queuing batches to the portable thread pool. Publish it once with `dotnet publish handoff/Handoff.csproj -c Release -p:UseAppHost=false -o handoff-out`, then run `dotnet handoff-out/Handoff.dll 1 0` under each candidate runtime on the same four CPUs. Arguments specify items per batch and competing CPU-bound threads. The controls in this investigation used `1 0`, `1 2` and `32 0`. It reports work items/s, whole-process CPU per item and sampled queue-to-execution percentiles.
