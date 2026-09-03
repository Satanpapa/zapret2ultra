using System.Diagnostics;
using System.Text;

namespace Zapret2Ultra.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, string? workingDirectory = null, string? stdin = null, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken).AsTask();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken).AsTask();
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
