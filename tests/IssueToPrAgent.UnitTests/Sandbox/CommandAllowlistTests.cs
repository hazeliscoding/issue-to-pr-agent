using IssueToPrAgent.Domain;
using IssueToPrAgent.Domain.Sandbox;

namespace IssueToPrAgent.UnitTests.Sandbox;

public class CommandAllowlistTests
{
    private static readonly CommandAllowlist Allowlist = new();

    [Theory]
    [InlineData("dotnet", "build")]
    [InlineData("dotnet", "test")]
    [InlineData("dotnet", "format")]
    [InlineData("dotnet", "restore")]
    [InlineData("npm", "test")]
    [InlineData("pnpm", "test")]
    [InlineData("git", "diff")]
    [InlineData("git", "status")]
    public void Allows_vetted_commands(string executable, string subcommand)
    {
        Allowlist.EnsureAllowed(SandboxCommand.Create(executable, subcommand));
    }

    [Theory]
    [InlineData("curl", "https://evil.example")]
    [InlineData("wget", "https://evil.example")]
    [InlineData("ssh", "host")]
    [InlineData("bash", "-c")]
    [InlineData("sh", "-c")]
    [InlineData("pwsh", "-c")]
    [InlineData("powershell", "-Command")]
    [InlineData("cmd", "/c")]
    [InlineData("dotnet", "nuget")]   // safe tool, unlisted sub-command
    [InlineData("git", "push")]       // read-only git only
    [InlineData("npm", "publish")]
    public void Denies_everything_else(string executable, string subcommand)
    {
        Assert.Throws<CommandDeniedException>(() =>
            Allowlist.EnsureAllowed(SandboxCommand.Create(executable, subcommand)));
    }

    [Fact]
    public void Denies_a_listed_executable_with_no_subcommand()
    {
        Assert.Throws<CommandDeniedException>(() => Allowlist.EnsureAllowed(SandboxCommand.Create("dotnet")));
    }

    [Theory]
    [InlineData("/usr/bin/git")]
    [InlineData("git.exe")]
    [InlineData("GIT")]
    public void Matches_the_executable_by_bare_name(string executable)
    {
        // A full path or an extension can't smuggle a tool past the check, nor bypass one.
        Assert.True(Allowlist.IsAllowed(SandboxCommand.Create(executable, "status")));
    }

    [Fact]
    public void A_disguised_blocked_tool_is_still_denied()
    {
        Assert.False(Allowlist.IsAllowed(SandboxCommand.Create(@"C:\tools\curl.exe", "diff")));
    }
}
