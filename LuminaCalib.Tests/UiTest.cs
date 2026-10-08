using Avalonia.Headless;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>Runs UI assertions through Avalonia's headless session with xUnit 4.</summary>
internal static class UiTest
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AvaloniaAppTestBase).Assembly);

    internal static Task Run(Action action) =>
        Session.Dispatch(action, TestContext.Current.CancellationToken);

    internal static Task Run(Func<Task> action) =>
        Session.Dispatch(async () =>
        {
            await action();
            return true;
        }, TestContext.Current.CancellationToken);
}
