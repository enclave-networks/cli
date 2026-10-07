namespace Enclave.Cli.Core;

/// <summary>
/// A flag value with an optional label after "=", such as "10.0.0.0/16=Office LAN".
/// </summary>
/// <param name="Value">The part before the first "=", or the whole text when it has none.</param>
/// <param name="HasLabel">Whether the text had an "="; without one an update keeps the label the
/// entry has.</param>
/// <param name="Label">The part after the first "=", or null when it is empty or there is no "=".
/// An empty label removes the label.</param>
internal readonly record struct LabelledValue(string Value, bool HasLabel, string? Label)
{
    // The split is at the first "=", which no range, country code, ACL or subnet contains, so a
    // label can itself contain "=" (proposed-cli-surface.md "Command options").
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
