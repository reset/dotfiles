using System.Diagnostics;
using System.Text;

namespace BurnDisc.Infrastructure;

internal sealed class ProcessResult {
    public ProcessResult(int exitCode, string output) {
        ExitCode = exitCode;
        Output = output;
    }

    public int ExitCode { get; }
    public string Output { get; } // combined stdout + stderr, one token per line

    public bool Succeeded => ExitCode == 0;
}

//
// Runs an external process, streaming its output as it arrives. Tokens are
// split on BOTH '\r' and '\n' — tools like cdrdao, chdman and 7z redraw a
// single status line with carriage returns, so line-based reads would never
// fire until the tool finished. The onToken callback is serialized across the
// stdout and stderr pumps, so callers don't need their own locking.
//
internal interface IProcessRunner {
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        Action<string>? onToken = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ProcessRunner : IProcessRunner {
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        Action<string>? onToken = null,
        CancellationToken cancellationToken = default) {
        ProcessStartInfo startInfo = new() {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };

        try {
            _ = process.Start();
        } catch (Exception ex) {
            throw new ProcessException($"Failed to launch '{fileName}': {ex.Message}", ex);
        }

        StringBuilder capture = new();
        object sync = new();

        // On cancellation, kill the child (and its tree) so an aborted burn
        // actually stops cdrdao rather than orphaning it.
        await using CancellationTokenRegistration registration = cancellationToken.Register(static state => {
            Process p = (Process)state!;
            try {
                if (!p.HasExited) {
                    p.Kill(entireProcessTree: true);
                }
            } catch {
                // process already gone / not killable — nothing to do
            }
        }, process);

        Task pumpOut = PumpAsync(process.StandardOutput, capture, onToken, sync, cancellationToken);
        Task pumpErr = PumpAsync(process.StandardError, capture, onToken, sync, cancellationToken);

        await Task.WhenAll(pumpOut, pumpErr).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, capture.ToString());
    }

    private static async Task PumpAsync(
        TextReader reader, StringBuilder capture, Action<string>? onToken, object sync, CancellationToken cancellationToken) {
        char[] buffer = new char[1024];
        StringBuilder token = new();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) {
            for (int i = 0; i < read; i++) {
                char ch = buffer[i];
                if (ch is '\r' or '\n') {
                    Flush(token, capture, onToken, sync);
                } else {
                    _ = token.Append(ch);
                }
            }
        }
        Flush(token, capture, onToken, sync);
    }

    private static void Flush(StringBuilder token, StringBuilder capture, Action<string>? onToken, object sync) {
        if (token.Length == 0) {
            return;
        }
        string line = token.ToString();
        _ = token.Clear();
        lock (sync) {
            _ = capture.Append(line).Append('\n');
            onToken?.Invoke(line);
        }
    }
}
