namespace IssueToPrAgent.Domain.Sandbox;

/// <summary>
/// The deterministic execution allowlist — the security boundary for what the agent may run.
/// Default-deny: an executable is permitted only if it is listed, and then only with one of its
/// listed sub-commands. Everything else (curl, wget, ssh, a shell, an unlisted sub-command like
/// <c>dotnet nuget push</c> or <c>git push</c>) is refused. The executable is matched by its bare
/// name, so a full path or an <c>.exe</c> suffix can't smuggle a blocked tool past the check.
/// </summary>
public sealed class CommandAllowlist
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> _allowed;

    /// <summary>The default policy: build/test/format tooling and read-only git, nothing else.</summary>
    public CommandAllowlist()
        : this(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = new(StringComparer.OrdinalIgnoreCase) { "build", "test", "format", "restore" },
            ["npm"] = new(StringComparer.OrdinalIgnoreCase) { "test", "ci" },
            ["pnpm"] = new(StringComparer.OrdinalIgnoreCase) { "test", "install" },
            ["git"] = new(StringComparer.OrdinalIgnoreCase) { "diff", "status" },
        })
    {
    }

    /// <summary>Constructs an allowlist from an explicit policy (used by tests).</summary>
    public CommandAllowlist(IReadOnlyDictionary<string, HashSet<string>> allowed) => _allowed = allowed;

    /// <summary>True if <paramref name="command"/> is permitted by the policy.</summary>
    public bool IsAllowed(SandboxCommand command)
    {
        var executable = Normalize(command.Executable);
        if (!_allowed.TryGetValue(executable, out var subcommands))
        {
            return false;
        }

        var subcommand = command.Arguments.Count > 0 ? command.Arguments[0] : null;
        return subcommand is not null && subcommands.Contains(subcommand);
    }

    /// <summary>Throws <see cref="CommandDeniedException"/> unless <paramref name="command"/> is permitted.</summary>
    public void EnsureAllowed(SandboxCommand command)
    {
        if (!IsAllowed(command))
        {
            var subcommand = command.Arguments.Count > 0 ? command.Arguments[0] : "(none)";
            throw new CommandDeniedException(
                $"Command '{command.Executable} {subcommand}' is not on the execution allowlist.");
        }
    }

    // Compare on the bare executable name so "/usr/bin/git", "git", and "GIT.EXE" all match "git".
    private static string Normalize(string executable) =>
        Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
}
