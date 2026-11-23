# Source attribution for the Linux socket throughput regression

## Observable change

One net10.0 service publication, one ASP.NET Core 10.0.12 shared-framework tree, and one .NET 10 load driver were used across the stock .NET 10.0.12 and .NET 11.0.0 servers. In the original four-run release comparison the throughput medians were 123,435 → 84,370 HTTP requests/s and 88,674 → 64,607 WebSocket round trips/s. The CPU-time changes were 28.91 → 29.99 µs/HTTP request and 36.01 → 33.65 µs/WebSocket round trip. Whole-service managed allocations were 12,286 → 12,276 B/HTTP request and 12,918 → 12,870 B/WebSocket round trip.

Separate diagnostic runs found that the time from epoll reporting a readable socket to entering the successful receive rose from 21.0 to 95.1 µs on HTTP and from 14.7 to 39.4 µs on WebSocket. The receive syscall itself averaged 0.8–1.1 µs. Reply arrival to the client process reading it also took longer. These intervals include time waiting to run on the host.

## Matched source builds

| Source change | Transport | Parent, requests/s | Commit, requests/s | Change | Parent → commit CPU µs/request | Parent → commit managed B/request |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| December 11, 2025 | WebSocket | 88,727 | 80,349 | −9.4% | 36.88 → 36.34 | 12,900 → 12,887 |
| December 11, 2025 | HTTP | 114,269 | 106,459 | −6.8% | 30.82 → 31.02 | 12,315 → 12,302 |
| January 6, 2026 | WebSocket | 79,919 | 58,636 | −26.6% | 35.76 → 31.87 | 12,861 → 12,877 |
| January 6, 2026 | HTTP | 103,495 | 79,238 | −23.4% | 31.32 → 31.19 | 12,305 → 12,310 |

The earliest measured parent/child step is `421445ed64461414dc1e99c6716e81e0b1c51b44`, with parent `0b723b3091814bcb89a3989f4cf4d7ae15c9fbca`. It moves CoreCLR to the managed wait subsystem on Unix and removes a multiplier from the thread-pool semaphore's spin count. The WebSocket ranges separate. The HTTP ranges overlap. Both builds used Linux-native Release CoreCLR, JIT, CoreLib and System.Native from their respective commit on the same December framework. [Runtime commit](https://github.com/dotnet/runtime/commit/421445ed64461414dc1e99c6716e81e0b1c51b44)

The largest measured step is `c14473701d9106c7be977b25b90211b99231b54b`, with parent `c026ba8fc4dea8f194f5e0d1889145a59534fad8`. It changes the portable pool's work-request protocol, semaphore and spinning policy. Its shipping product changes are in CoreLib. Each side used a Release linux-x64 CoreLib built on Windows, with the identical January native engine, JIT, other framework binaries and matching Crossgen2. On both protocols every parent sample exceeded every child sample. The two changes have different baselines. Their percentages cannot be added to assign shares of the release difference. [Runtime commit](https://github.com/dotnet/runtime/commit/c14473701d9106c7be977b25b90211b99231b54b)

Both measured changes preceded the August 2026 socket dispatch tree. A separate regression reported against .NET 11 Preview 7 recovered in RC2 on its author's workload. The present GA observation uses a different application and CPU placement. [Socket dispatch tree](https://github.com/dotnet/runtime/pull/131177) · [Earlier ASP.NET regression](https://github.com/dotnet/runtime/issues/132951)

The historical syscall traces also locate a change before the successful receive. At the December edge the mean ready-to-receive-entry interval rose from 11.5 to 28.2 µs on WebSocket and from 17.7 to 26.3 µs on HTTP. At the January edge it rose from 11.9 to 28.9 µs on WebSocket and from 20.8 to 28.2 µs on HTTP. Each arm had one separate diagnostic run per protocol. [Per-trace counts and distributions](syscall-timings.json)

## Isolating the January change

On the January commit, a diagnostic build replaced just the nine-iteration exponential backoff in the semaphore with the parent's 70-iteration wait loop, which interleaves short spins and scheduler yields. It retained the commit's new request protocol, queues and semaphore accounting. On paired restarts, WebSocket throughput rose from 53,580 to 83,978/s and HTTP rose from 77,213 to 118,492/s. The two individual WebSocket observations with the old wait loop were 83,005 and 84,951/s. The paired unmodified observations were 46,394 and 60,766/s. Whole-service WebSocket CPU rose from 32.09 to 39.36 µs/request, with about 12.86 KB of managed allocations per completion in both arms.

This reversal identifies the change to the wait policy inside January's commit as a contributor on this workload. The diagnostic restores the old polling cadence and budget together. The proposed patch on current source uses the existing budget, checks for a signal and yields on alternating capped spins. The January diagnostic is separate from the proposed patch.

## How the source comparisons were run

Each historical comparison used independent service launches in parent, child, child, parent order. Each launch primed both protocols before collecting two ten-second observations per transport with four seconds of warm-up per observation. The protocol order alternated. Each row above gives the median of four observations from two independent server launches. All 32 timed observations for the two historical pairs had zero request errors, timeouts, reconnects and reply-validation failures. The raw CSV identifies launch and observation. The earlier preview and candidate screens overlapped compilation and were excluded from these results.

The machine was an Intel Core i9-12900KF with eight Linux guest CPUs under Docker Desktop on WSL2. The guest reported CPUs 0/1, 2/3, 4/5 and 6/7 as sibling pairs. The server used 0–3, the client used 6–7, and the database used 4–5. The 32 connections each kept one request outstanding. The source pairs retained a minimum of 256 worker threads and disabled dynamic GC adaptation on the server. The public standalone release runner uses the framework defaults unless explicitly overridden.

Each paired CoreLib was compiled in Release and precompiled with the same generation of Crossgen2, `-O --inputbubble --targetarch:x64 --targetos:linux`, without an official profile input. Use the unmodified source build of each pair as its baseline. Official release results are labeled separately. In the CSV, `parent-dec` and `parent-jan` are the historical pairs, `jan-ablation` is the diagnostic reversal, `ga-confirmed` is the submitted policy on GA, and `main-checked` is the submitted policy on the pinned nightly. `ga-paired` and `ga-shortpolls` are earlier alternatives. CPU is process time per completed request. Allocations count managed bytes across the entire server process.

The release gap depends on the driver. With the product's own settings and two driver CPUs, .NET 10 led by 29.5% on HTTP and 18.0% on WebSocket. Giving that same driver four CPUs changed the differences to −2.2% on HTTP and +9.6% on WebSocket. Confirmation on native Linux and on a separate load machine would establish how broadly these particular figures apply.

Binary identities are in [provenance.json](provenance.json). Timed observations are in [source-comparisons.csv](source-comparisons.csv). The standalone public workload and source-build commands are in [the reproducer](repro/README.md).

The release branch at `f465948a45b04f0aefca468e8a3d3a26b0d3376c` still has the GA versions of `LowLevelLifoSemaphore.cs` and `Backoff.cs`. The proposed upstream-main commit and the measured .NET 12 nightly likewise shared identical source for the two affected files before applying the patch.
