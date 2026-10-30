namespace Enclave.Cli.Core;

/// <summary>
/// Without "=" (HasLabel false), an update keeps the label the entry has; an empty label (Label
/// null) removes it.
/// </summary>
internal readonly record struct LabelledValue(string Value, bool HasLabel, string? Label)
{
    // The split is at the first "=", which no range, country code, ACL or subnet contains, so a
    // label can itself contain "=" (spec/cli-surface.md "Command options").
    public static LabelledValue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf('=', StringComparison.Ordinal);

        if (split < 0)
        {
            return new LabelledValue(text, HasLabel: false, Label: null);
        }

        var label = text[(split + 1)..];
        return new LabelledValue(text[..split], HasLabel: true, Label: label.Length == 0 ? null : label);
    }
}
