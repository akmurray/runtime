# Linux thread-pool patch validation

All comparisons use medians across separately timed runs. The attached CSVs keep the individual observations. CPU measures process CPU time. Managed allocations are entire-process bytes divided by successful completions.

## Authenticated 32-client workload

| Runtime | Protocol | Requests/s before → after | Change | CPU µs/request | Managed B/request | Client p99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 11.0.0 | WS | 63,952 → 79,814 | +24.8% | 33.64 → 35.23 | 12,890 → 12,877 | 1.95 → 1.94 |
| 11.0.0 | HTTP | 83,726 → 95,605 | +14.2% | 30.06 → 29.48 | 12,277 → 12,265 | 1.65 → 1.66 |
| 12 alpha | WS | 64,096 → 80,443 | +25.5% | 33.83 → 34.94 | 12,865 → 12,877 | 2.04 → 1.38 |
| 12 alpha | HTTP | 82,750 → 100,414 | +21.3% | 30.08 → 27.98 | 12,273 → 12,288 | 1.71 → 1.65 |

Each arm had two independently restarted servers per runtime. Each .NET 11 launch supplied two readings per protocol. Each .NET 12 launch supplied one. All 19,451,391 requests completed without a reported error, timeout, reconnect or sampled reply-validation failure. The tested nightly was 12.0.0-alpha.1.26573.102. [Historical source builds and platform details](HISTORY.md)

## Public release comparison

The standalone echo and JSON application ran without a database and used framework-default worker and GC settings. The comparison is stock 10.0.12 → stock 11.0.0, with the exact same ASP.NET Core 10.0.12 files in both. The .NET 10 client and its CPU placement were fixed across arms.

| Application | Route | Clients | Requests/s before → after | Change | CPU µs/request | Managed B/request | Client p99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SocketRepro | /ws | 32 | 180,603 → 123,441 | -31.7% | 19.09 → 18.70 | 0.00 → 0.01 | 0.524 → 0.627 |
| SocketRepro | /dispatch | 32 | 184,072 → 123,448 | -32.9% | 19.49 → 18.74 | 173.39 → 150.00 | 0.537 → 0.609 |
| SocketRepro | /json | 32 | 153,854 → 110,341 | -28.3% | 22.10 → 20.95 | 464.03 → 464.03 | 0.836 → 0.860 |

## Public workload on the proposed .NET 11 patch

Both sides use the release GA source, precompiled the same way, with the tested policy in B. The public runner verified the mounted CoreLib/native images and fixed Kestrel on every launch. These runs used the same thread-pool and GC overrides as the authenticated workload.

| Application | Route | Clients | Requests/s before → after | Change | CPU µs/request | Managed B/request | Client p99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SocketRepro | /ws | 32 | 101,209 → 143,317 | +41.6% | 23.56 → 20.81 | 0.01 → 0.02 | 1.268 → 0.820 |
| SocketRepro | /dispatch | 32 | 113,940 → 152,644 | +34.0% | 20.23 → 20.49 | 148.54 → 151.17 | 0.741 → 0.613 |
| SocketRepro | /json | 32 | 105,263 → 140,829 | +33.8% | 21.90 → 21.28 | 464.13 → 464.03 | 1.074 → 0.820 |

## Public workload on the upstream nightly patch

These runs used framework-default thread-pool and GC settings. Minimal and PlatformBenchmarks are the Minimal API and raw Kestrel JSON implementations from aspnet/Benchmarks at ea92011aadd0e477a3cccf266d9589f5cdf131e7. Each route and concurrency had two independently restarted servers per arm. The driver verified successful HTTP status codes and sampled the response contents. WebSocket frame type and message length were checked on every exchange.

| Application | Route | Clients | Requests/s before → after | Change | CPU µs/request | Managed B/request | Client p99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SocketRepro | /ws | 32 | 106,385 → 148,223 | +39.3% | 20.02 → 18.42 | 0.01 → 0.01 | 0.633 → 0.460 |
| SocketRepro | /dispatch | 32 | 108,763 → 148,422 | +36.5% | 18.82 → 18.89 | 149.45 → 153.89 | 0.686 → 0.503 |
| SocketRepro | /json | 32 | 112,489 → 160,529 | +42.7% | 20.57 → 19.59 | 464.06 → 464.09 | 0.851 → 0.742 |
| Minimal | /json | 32 | 114,934 → 164,814 | +43.4% | 20.00 → 19.17 | 160.02 → 160.03 | 0.855 → 0.740 |
| Minimal | /json | 256 | 118,734 → 160,414 | +35.1% | 20.01 → 19.55 | 160.10 → 160.02 | 4.799 → 3.582 |
| PlatformBenchmarks | /json | 32 | 116,555 → 169,981 | +45.8% | 19.36 → 18.76 | 0.00 → 0.01 | 0.830 → 0.624 |
| PlatformBenchmarks | /json | 256 | 119,928 → 163,384 | +36.2% | 19.43 → 19.39 | 0.01 → 0.01 | 4.724 → 3.994 |

## Non-socket worker scheduling control

One producer queues the requested batch and waits for completion. The process uses the same four service CPUs. The two competing threads, where present, remain runnable. CPU includes those competing threads.

| Work items per batch | Competing CPU-bound threads | Items/s before → after | Change | Whole-process CPU µs/item | Queue-to-start p50 µs | Queue-to-start p99 µs |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 0 | 90,348 → 134,185 | +48.5% | 26.42 → 24.16 | 1.14 → 0.86 | 23.47 → 7.14 |
| 1 | 2 | 76,690 → 136,890 | +78.5% | 43.58 → 29.20 | 1.33 → 0.99 | 24.30 → 4.61 |
| 32 | 0 | 1,837,754 → 2,974,287 | +61.8% | 1.25 → 1.20 | 1.26 → 1.00 | 18.00 → 4.26 |

## Readiness profiles

The syscall observer records epoll returning a readable socket and entry to and return from a successful recv or recvmsg. Each diagnostic run joins complete cycles by connection, with exactly one readable event and a matching receive in a completed send-to-send interval. All 32 connections contributed to every row. These separate instrumented runs locate the wait within a request. They do not identify which other thread ran while a worker was waiting.

| Build | Protocol | Matched cycles | Ready → recv mean µs | Median µs | p99 µs | Successful recv mean µs |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| winbase | WS | 126,057 / 126,129 | 35.21 | 4.73 | 223.93 | 0.80 |
| winbase | HTTP | 171,392 / 171,624 | 84.49 | 57.17 | 405.33 | 0.81 |
| checked-yield | WS | 162,780 / 162,883 | 24.01 | 7.53 | 186.41 | 1.15 |
| checked-yield | HTTP | 203,435 / 203,884 | 71.34 | 50.23 | 355.67 | 1.10 |
| jan-pre | WS | 152,492 / 152,685 | 11.92 | 1.93 | 108.55 | 0.81 |
| jan-pre | HTTP | 208,653 / 210,331 | 20.81 | 8.39 | 166.37 | 1.20 |
| jan-post | WS | 99,934 / 99,979 | 28.86 | 12.60 | 147.17 | 0.73 |
| jan-post | HTTP | 167,843 / 168,036 | 28.22 | 11.22 | 188.62 | 0.77 |
| dec-pre | WS | 197,821 / 198,015 | 11.48 | 1.84 | 62.40 | 0.82 |
| dec-pre | HTTP | 226,322 / 228,516 | 17.65 | 7.55 | 125.89 | 1.04 |
| dec-post | WS | 109,413 / 109,598 | 28.24 | 7.35 | 196.70 | 1.32 |
| dec-post | HTTP | 208,449 / 209,931 | 26.31 | 10.34 | 175.76 | 1.36 |

## Build and functional verification

The submitted source commit is `6524b39b0871ca767e13460dd205520c38576c7f`, based on `6433d426c81f03f19f5f39c7ca6a88fc3e4be815` from `dotnet/runtime:main`. A Release linux-x64 CoreLib cross-build of that commit completed with zero warnings and zero errors. Applying the formatted patch to a clean index at the base commit reproduced the submitted Git tree.

The upstream `System.Threading.ThreadPool.Tests` suite ran under the patched Linux x64 nightly with the repository-generated test selection. Of 70 tests, 68 passed and two were skipped. This included work-queue depletion, concurrent work items and registered waits. An earlier manual invocation omitted the repository's platform filtering and selected ten Windows-only cases on Linux. The verified invocation used the generated settings with its host executable path mapped to the Linux container.

The attached public recipe also completed a fresh Linux-native build of each .NET 11.0.0 variant. With framework defaults and 32 clients, the short end-to-end verification completed 2,087,710 WebSocket and HTTP requests across four runs. Each server mapped the expected source-built CoreLib and the same Kestrel binary. The paired performance results above use the longer runs. [Build identities, test counts and per-run verification](verification.json)
