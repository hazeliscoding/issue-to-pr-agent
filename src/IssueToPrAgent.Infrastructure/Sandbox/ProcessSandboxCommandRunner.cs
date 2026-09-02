using System.ComponentModel;
using System.Diagnostics;
using IssueToPrAgent.Application.Sandbox;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Repository;
using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.Infrastructure.Sandbox;

/// <summary>
/// Runs a command as a child process rooted at the workspace, with no shell
/// (<see cref="ProcessStartInfo.UseShellExecute"/> false), a hard timeout that kills the whole
/// process tree, and bounded output capture. It assumes the command was already vetted by the
/// allowlist upstream — its job is safe mechanics, not policy.
/// </summary>
public sealed class ProcessSandboxCommandRunner(RepositoryWorkspace workspace) : ISandboxCommandRunner
{
    public async Task<CommandResult> RunAsync(
        SandboxCommand command, SandboxRunOptions options, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Executable,
            WorkingDirectory = workspace.Root,
            UseShellExecute = false, // no shell → no metacharacter interpretation, no shell injection
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stdout = new BoundedTextBuffer(options.MaxOutputChars);
        var stderr = new BoundedTextBuffer(options.MaxOutputChars);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new DomainRuleException($"Could not start '{command.Executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        using var timeoutCts = new CancellationTokenSource(options.EffectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);

            if (!timedOut)
            {
                throw; // caller-requested cancellation, not a timeout — propagate
            }
        }

        process.WaitForExit(); // ensure the async output handlers have drained
        stopwatch.Stop();

        return new CommandResult(
            CommandId: Guid.NewGuid(),
            ExitCode: process.ExitCode,
            Stdout: stdout.ToString(),
            Stderr: stderr.ToString(),
            Duration: stopwatch.Elapsed,
            TimedOut: timedOut,
            OutputTruncated: stdout.Truncated || stderr.Truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the timeout and the kill — nothing to do.
        }
    }
}
