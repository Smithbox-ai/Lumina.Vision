using Xunit;

// Disable test parallelization for Avalonia UI tests.
// Avalonia's headless mode requires sequential execution to avoid race conditions
// when initializing and managing the application lifecycle.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
