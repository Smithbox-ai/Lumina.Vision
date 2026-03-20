using LuminaCalib.Services;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Тесты разделения потоков логирования: файл только для исключений, UI — для всех сообщений.
/// </summary>
public sealed class ExceptionLoggerTests
{
    private static readonly string DefaultLogPath = Path.Combine(AppContext.BaseDirectory, "Exceptions.txt");

    [Fact]
    public void LogMessage_DoesNotWriteToExceptionsFile()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ExceptionLogger_{Guid.NewGuid():N}.txt");
        try
        {
            ExceptionLogger.SetLogFilePath(logPath);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            ExceptionLogger.LogMessage("Info line", "UnitTest");

            Assert.False(File.Exists(logPath));
        }
        finally
        {
            RestoreLoggerPath();
            TryDeleteFile(logPath);
        }
    }

    [Fact]
    public void LogException_WritesToExceptionsFile()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ExceptionLogger_{Guid.NewGuid():N}.txt");
        try
        {
            ExceptionLogger.SetLogFilePath(logPath);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            ExceptionLogger.LogException(new InvalidOperationException("boom"), "UnitTest");

            Assert.True(File.Exists(logPath));
            var text = File.ReadAllText(logPath);
            Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
            Assert.Contains("boom", text, StringComparison.Ordinal);
        }
        finally
        {
            RestoreLoggerPath();
            TryDeleteFile(logPath);
        }
    }

    [Fact]
    public void OnLogLine_ReceivesLines_ForMessageAndException()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ExceptionLogger_{Guid.NewGuid():N}.txt");
        var lines = new List<string>();

        try
        {
            ExceptionLogger.SetLogFilePath(logPath);
            ExceptionLogger.OnLogLine += lines.Add;

            ExceptionLogger.LogMessage("message-line", "UnitTest");
            ExceptionLogger.LogException(new Exception("exception-line"), "UnitTest");

            Assert.Contains(lines, line => line.Contains("message-line", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("exception-line", StringComparison.Ordinal));
        }
        finally
        {
            ExceptionLogger.OnLogLine -= lines.Add;
            RestoreLoggerPath();
            TryDeleteFile(logPath);
        }
    }

    private static void RestoreLoggerPath()
    {
        ExceptionLogger.SetLogFilePath(DefaultLogPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
