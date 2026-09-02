using System.Text;

namespace IssueToPrAgent.Infrastructure.Sandbox;

/// <summary>
/// Accumulates process output up to a character budget, then stops and records that it did.
/// Keeps a runaway command from exhausting memory while still preserving the leading output,
/// which is where the useful signal (the first error) almost always is.
/// </summary>
internal sealed class BoundedTextBuffer(int maxChars)
{
    private readonly StringBuilder _builder = new();

    public bool Truncated { get; private set; }

    public void AppendLine(string line)
    {
        if (Truncated)
        {
            return;
        }

        var remaining = maxChars - _builder.Length;
        if (remaining <= 0)
        {
            Truncated = true;
            return;
        }

        if (line.Length + 1 > remaining)
        {
            _builder.Append(line.AsSpan(0, remaining));
            Truncated = true;
            return;
        }

        _builder.Append(line).Append('\n');
    }

    public override string ToString() => _builder.ToString();
}
