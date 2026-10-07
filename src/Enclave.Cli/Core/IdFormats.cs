using System.Globalization;
using System.Text.RegularExpressions;

namespace Enclave.Cli.Core;

/// <summary>
/// The ID forms of proposed-cli-surface.md "ID checks". Every ID is checked against its form before
/// any call, since Enclave.Sdk.Api 1.0.5 puts IDs into URL paths unescaped and .NET resolves ".."
/// when it combines a path with the base address (UnapprovedSystemsClient.cs:95): a system ID of
/// "../systems/ABCDE" given to decline would revoke system ABCDE.
/// </summary>
internal static partial class IdFormats
{
    /// <summary>
    /// A system ID: ASCII letters and digits.
    /// </summary>
    public static IdFormat<string> System { get; } = new(
        "letters and digits",
        text => (text.Length > 0 && text.All(char.IsAsciiLetterOrDigit), text));

    /// <summary>
    /// A key, policy, zone, hostname or trust requirement ID: a whole number that fits in 32 bits
    /// (portal Enclave.Configuration.Data/Identifiers, IdBackingType.Int). No sign, spaces,
    /// separators or hexadecimal.
    /// </summary>
    public static IdFormat<int> Int32 { get; } = new(
        "a whole number",
        text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, 0));

    /// <summary>
    /// A tag name, by the API's rule (portal TagValidationExtensions.cs:13): lower-case letters and
    /// digits in words joined by single hyphens or dots.
    /// </summary>
    public static IdFormat<string> Tag { get; } = new(
        "lower-case letters and digits, in words joined by single hyphens or dots",
        text => (TagPattern().IsMatch(text), text));

    /// <summary>
    /// An organisation, account or partner ID: a GUID, with hyphens (as the portals show it) or
    /// without (as the API writes it).
    /// </summary>
    public static IdFormat<Guid> Guid { get; } = new(
        "a GUID",
        text => global::System.Guid.TryParseExact(text, "D", out var value) || global::System.Guid.TryParseExact(text, "N", out value)
            ? (true, value)
            : (false, global::System.Guid.Empty));

    // \z, since $ also matches before a final newline, which would let "web\n" through.
    [GeneratedRegex(@"^([a-z0-9]+[-.])*[a-z0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
