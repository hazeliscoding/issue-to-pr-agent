namespace IssueToPrAgent.Domain.Sandbox;

/// <summary>
/// A command to run: an executable and its arguments as a list (never a shell string). Passing
/// arguments as a list means the OS receives them verbatim — no shell parses them — so
/// metacharacters like <c>;</c> or <c>&amp;&amp;</c> can't chain a second command. This shape is
/// half of why the sandbox is safe; the allowlist is the other half.
/// </summary>
public sealed record SandboxCommand(string Executable, IReadOnlyList<string> Arguments)
{
    public static SandboxCommand Create(string executable, params string[] arguments) =>
        new(executable, arguments);
}
