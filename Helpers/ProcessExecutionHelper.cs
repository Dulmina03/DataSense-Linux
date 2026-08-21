using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DataSense.Helpers;

public class ProcessExecutionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; } = -1;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public Exception? Exception { get; init; }
}

public static class ProcessExecutionHelper
{
    public static async Task<ProcessExecutionResult> ExecuteAsync(
        string fileName,
        string[]? arguments = null,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new ProcessExecutionResult
            {
                Success = false,
                StandardError = "Executable filename cannot be null or empty."
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (arguments != null && arguments.Length > 0)
        {
            foreach (var arg in arguments)
            {
                startInfo.ArgumentList.Add(arg ?? string.Empty);
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderrBuilder.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessExecutionResult
                {
                    Success = false,
                    StandardError = $"Failed to start process '{fileName}'."
                };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(cts.Token);
                return new ProcessExecutionResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    StandardOutput = stdoutBuilder.ToString().Trim(),
                    StandardError = stderrBuilder.ToString().Trim()
                };
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { /* Best-effort process kill */ }

                return new ProcessExecutionResult
                {
                    Success = false,
                    TimedOut = true,
                    StandardError = $"Process execution timed out after {timeoutMs} ms."
                };
            }
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult
            {
                Success = false,
                Exception = ex,
                StandardError = ex.Message
            };
        }
    }
}
