using Xunit.Sdk;
using Xunit.v3;

// UI tests drive real windows and must not run concurrently.
[assembly: Parallelization(Mode = ParallelMode.None)]
