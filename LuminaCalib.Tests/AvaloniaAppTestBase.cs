using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(LuminaCalib.Tests.AvaloniaAppTestBase))]

namespace LuminaCalib.Tests;

/// <summary>
/// Base class for Avalonia UI tests. This initializes the Avalonia application
/// once for all tests, enabling headless testing of Views and ViewModels.
/// </summary>
public class AvaloniaAppTestBase
{
    /// <summary>
    /// Builds the Avalonia application for headless testing.
    /// This method is called once by the [AvaloniaTestApplication] attribute.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LuminaCalib.App>()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
