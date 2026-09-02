using System.Text;
using IssueToPrAgent.Application.Repository;
using IssueToPrAgent.Application.Sandbox;
using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Analysis;
using IssueToPrAgent.Domain.Fixing;
using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.Application.Fixing;

/// <summary>
/// The deterministic test-first fix loop. The model only proposes content (a reproduction test,
/// then a fix); this workflow owns the sequence and every gate: it writes files through the
/// contained writer, builds and tests through the sandbox, and decides pass/fail from exit codes.
/// Its central invariant is enforced structurally — implementation code is never changed until a
/// reproduction test has been confirmed to <em>fail</em> (build succeeds, test fails). If that
/// can't be established, it stops and explains instead of guessing at a fix.
/// </summary>
public sealed class TestFirstFixWorkflow(
    IFileReader reader,
    IFileWriter writer,
    SandboxTools sandbox,
    FixPlanner planner,
    FixOptions options)
{
    public async Task<FixOutcome> RunAsync(IssueContext issue, IssueAnalysis analysis, CancellationToken cancellationToken)
    {
        var commands = new List<CommandResult>();
        var applied = new List<FileOperation>();
        var context = await BuildContextAsync(analysis, cancellationToken);

        // Phase A — reproduce: a test that fails against the current code. No implementation changes.
        var (reproTestPath, failingOutput, reproNote) =
            await ReproduceAsync(issue, analysis, context, commands, applied, cancellationToken);

        if (reproTestPath is null)
        {
            return new FixOutcome(
                FixOutcomeStatus.CannotReproduce,
                $"Could not establish a failing reproduction test after {options.MaxReproductionAttempts} attempt(s). {reproNote}".Trim(),
                ReproductionTestPath: null,
                commands,
                applied);
        }

        // Phase B — fix: only reached once reproduction is confirmed.
        return await FixAsync(issue, analysis, context, reproTestPath, failingOutput!, commands, applied, cancellationToken);
    }

    private async Task<(string? ReproTestPath, string? FailingOutput, string Note)> ReproduceAsync(
        IssueContext issue,
        IssueAnalysis analysis,
        string context,
        List<CommandResult> commands,
        List<FileOperation> applied,
        CancellationToken cancellationToken)
    {
        string? note = null;
        for (var attempt = 0; attempt < options.MaxReproductionAttempts; attempt++)
        {
            var proposal = await planner.ProposeReproductionAsync(issue, analysis, context, note, cancellationToken);

            if (proposal.Operations.Count == 0)
            {
                note = "No reproduction test was proposed.";
                continue;
            }

            // Guard: reproduction may only touch test files — it must not change implementation code.
            var nonTest = proposal.Operations.FirstOrDefault(op => !IsTestPath(op.Path));
            if (nonTest is not null)
            {
                note = $"A reproduction may only create or edit test files, but it targeted '{nonTest.Path}'.";
                continue;
            }

            if (!await TryApplyAsync(proposal.Operations, applied, reason => note = reason, cancellationToken))
            {
                continue;
            }

            var build = await sandbox.RunBuildAsync(options.Project, cancellationToken);
            commands.Add(build);
            if (!Succeeded(build))
            {
                note = $"The reproduction test did not compile:\n{Tail(build)}";
                continue;
            }

            var test = await sandbox.RunTestsAsync(options.Project, cancellationToken);
            commands.Add(test);
            if (Succeeded(test))
            {
                note = "The reproduction test passed, but it must fail to demonstrate the bug.";
                continue;
            }

            // Build succeeded and tests failed → the bug is reproduced.
            var reproPath = proposal.Operations.First(op => IsTestPath(op.Path)).Path;
            return (reproPath, Tail(test), string.Empty);
        }

        return (null, null, note ?? string.Empty);
    }

    private async Task<FixOutcome> FixAsync(
        IssueContext issue,
        IssueAnalysis analysis,
        string context,
        string reproTestPath,
        string failingOutput,
        List<CommandResult> commands,
        List<FileOperation> applied,
        CancellationToken cancellationToken)
    {
        string? note = null;
        for (var attempt = 0; attempt < options.MaxFixAttempts; attempt++)
        {
            var proposal = await planner.ProposeFixAsync(issue, analysis, context, failingOutput, note, cancellationToken);

            if (proposal.Operations.Count == 0)
            {
                note = "No fix was proposed.";
                continue;
            }

            // Guard: a fix must not edit the reproduction test — it can't pass by weakening the test.
            if (proposal.Operations.Any(op => SamePath(op.Path, reproTestPath)))
            {
                note = "A fix must not modify the reproduction test.";
                continue;
            }

            if (!await TryApplyAsync(proposal.Operations, applied, reason => note = reason, cancellationToken))
            {
                continue;
            }

            var build = await sandbox.RunBuildAsync(options.Project, cancellationToken);
            commands.Add(build);
            if (!Succeeded(build))
            {
                note = $"The fix did not compile:\n{Tail(build)}";
                continue;
            }

            var test = await sandbox.RunTestsAsync(options.Project, cancellationToken);
            commands.Add(test);
            if (Succeeded(test))
            {
                return new FixOutcome(
                    FixOutcomeStatus.Fixed,
                    "The reproduction test fails before the change and passes after it.",
                    reproTestPath,
                    commands,
                    applied);
            }

            note = $"Tests still failing after the fix:\n{Tail(test)}";
            failingOutput = Tail(test);
        }

        return new FixOutcome(
            FixOutcomeStatus.FixFailed,
            $"Reproduced the bug but the reproduction test still fails after {options.MaxFixAttempts} fix attempt(s). {note}".Trim(),
            reproTestPath,
            commands,
            applied);
    }

    private async Task<bool> TryApplyAsync(
        IReadOnlyList<FileOperation> operations,
        List<FileOperation> applied,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.ApplyAsync(operations, cancellationToken);
            applied.AddRange(operations);
            return true;
        }
        catch (DomainException ex)
        {
            // Containment violation or a bad edit anchor — reject this proposal and let the loop retry.
            onFailure($"Could not apply the proposed changes: {ex.Message}");
            return false;
        }
    }

    private async Task<string> BuildContextAsync(IssueAnalysis analysis, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("Relevant files:\n");
        var included = 0;

        foreach (var area in analysis.SuspectedAreas)
        {
            if (included >= options.MaxContextFiles)
            {
                break;
            }

            try
            {
                var file = await reader.ReadAsync(
                    area, new FileReadOptions(MaxBytes: options.MaxContextBytesPerFile), cancellationToken);
                builder.Append("\n=== ").Append(file.Path).Append(" ===\n").Append(file.Text).Append('\n');
                included++;
            }
            catch (DomainException)
            {
                // A suspected area that isn't a readable file (a component name, a path that escapes) — skip it.
            }
        }

        return included == 0 ? "No suspected-area files could be read for context." : builder.ToString();
    }

    private static bool Succeeded(CommandResult result) => result is { ExitCode: 0, TimedOut: false };

    // A path is a test if any segment or the file name mentions "test" — covers *Tests projects and *Tests.cs.
    private static bool IsTestPath(string path) => path.Contains("test", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string Tail(CommandResult result)
    {
        var combined = $"{result.Stdout}\n{result.Stderr}".Trim();
        const int max = 2000;
        return combined.Length <= max ? combined : combined[^max..];
    }
}
