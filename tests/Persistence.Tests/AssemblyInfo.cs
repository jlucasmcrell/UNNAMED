// UNNAMED Persistence Tests - Assembly Info
// Assembly-level attributes for test configuration

using Xunit;

// Force tests to run sequentially to avoid file locking issues
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
