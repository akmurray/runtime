# Reproducing the Linux thread-pool throughput regression

Start with the [standalone Kestrel reproducer](repro/README.md) to compare .NET 10 and .NET 11 or rebuild the proposed fix. The scripts and measurements are also available in the [downloadable ZIP](socket-regression-evidence.zip).

- [Matched parent and child commits and wait-policy experiment](HISTORY.md)
- [Throughput, CPU, allocations, readiness latency and test results](RESULTS.md)
- [Individual measurements for the source builds](source-comparisons.csv) and [public workloads](public-comparisons.csv)
- [Component versions and hashes](provenance.json) and [functional verification](verification.json)
