using Xunit;

// All integration tests share a single PostgreSQL database (devhub_test),
// recreated per fixture. Parallel classes would race on schema recreation,
// so parallelization is disabled.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
