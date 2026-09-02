namespace IssueToPrAgent.Application.Fixing;

/// <summary>Configuration for the test-first fix workflow.</summary>
public sealed class FixOptions
{
    public const string SectionName = "Fix";

    /// <summary>Model used to propose the reproduction test and the fix.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>Repository-relative project/solution to build and test; null builds/tests the default.</summary>
    public string? Project { get; set; }

    /// <summary>How many times to ask for a reproduction test before giving up.</summary>
    public int MaxReproductionAttempts { get; set; } = 2;

    /// <summary>How many times to ask for a fix before giving up.</summary>
    public int MaxFixAttempts { get; set; } = 3;

    /// <summary>Byte budget per suspected-area file read for context.</summary>
    public int MaxContextBytesPerFile { get; set; } = 32 * 1024;

    /// <summary>How many suspected-area files to read for context.</summary>
    public int MaxContextFiles { get; set; } = 6;
}
