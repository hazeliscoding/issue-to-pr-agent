using System.Diagnostics;
using IssueToPrAgent.Application.Sandbox;
using IssueToPrAgent.Domain.Sandbox;
using IssueToPrAgent.Infrastructure.Sandbox;

namespace IssueToPrAgent.IntegrationTests.Sandbox;

/// <summary>
/// Exercises the process-execution mechanics against real child processes. Uses the OS shell
/// directly (not the sandbox tools) because these tests validate the executor's timeout, output
/// bounding, and exit-code handling — not the allowlist, which is covered by its own unit tests.
/// </summary>
public class ProcessSandboxCommandRunnerTests
{
    // A trivial shell command for the host OS — a controllable process to run.
    private static SandboxCommand Shell(string script) => OperatingSystem.IsWindows()
        ? new SandboxCommand("cmd.exe", ["/c", script])
        : new SandboxCommand("/bin/sh", ["-c", script]);

    private static ProcessSandboxCommandRunner RunnerOver(TempWorkspace ws) => new(ws.Workspace);

    [Fact]
    public async Task Captures_a_zero_exit_code_and_stdout()
    {
        using var ws = new TempWorkspace();

        var result = await RunnerOver(ws).RunAsync(Shell("echo hello"), new SandboxRunOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Stdout);
        Assert.False(result.TimedOut);
        Assert.NotEqual(Guid.Empty, result.CommandId);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Reports_a_nonzero_exit_code()
    {
        using var ws = new TempWorkspace();

        var result = await RunnerOver(ws).RunAsync(Shell("exit 7"), new SandboxRunOptions(), CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Times_out_and_terminates_a_long_running_command()
    {
        using var ws = new TempWorkspace();
        var sleep = OperatingSystem.IsWindows() ? "ping -n 20 127.0.0.1 > nul" : "sleep 20";

        var stopwatch = Stopwatch.StartNew();
        var result = await RunnerOver(ws).RunAsync(
            Shell(sleep), new SandboxRunOptions(Timeout: TimeSpan.FromSeconds(1)), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), "the process should have been killed near the timeout");
    }

    [Fact]
    public async Task Bounds_captured_output()
    {
        using var ws = new TempWorkspace();

        var result = await RunnerOver(ws).RunAsync(
            Shell("echo hello"), new SandboxRunOptions(MaxOutputChars: 3), CancellationToken.None);

        Assert.True(result.OutputTruncated);
        Assert.True(result.Stdout.Length <= 3);
    }
}
