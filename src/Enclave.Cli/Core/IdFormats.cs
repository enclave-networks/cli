using System.Globalization;
using System.Text.RegularExpressions;

namespace Enclave.Cli.Core;

// The ID forms of spec/cli-surface.md "ID checks".
// Enclave.Sdk.Api also escapes IDs in URL paths (ClientBase.PathSegment), so an ID such as
// "../systems/ABCDE" cannot move a request to another route.
internal static partial class IdFormats
{
    // System IDs are upper case (portal HashIdProvider; the sdk repository's
    // CertificateConstants.CertificateNameKeyspaceAlphabet). The system routes upper-case the ID they
    // are given (portal SingleSystemQueryHandler, BulkSystemUpdateHandler), but the policy gateway
    // check compares it exactly (PolicyGatewayValidator), so gw001 names no gateway there. Reading
    // IDs in upper case gives every request and duplicate check the form the API stores.
    public static IdFormat<string> System { get; } = new(
        "letters and digits",
        text => (text.Length > 0 && text.All(char.IsAsciiLetterOrDigit), text.ToUpperInvariant()));

    // Key, policy, zone, hostname and trust requirement IDs are 32-bit (portal
    // Enclave.Configuration.Data/Identifiers, IdBackingType.Int).
    public static IdFormat<int> Int32 { get; } = new(
        "a whole number",
        text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, 0));

    // The API's tag rule (portal TagValidationExtensions, _tagRegex).
    public static IdFormat<string> Tag { get; } = new(
        "lower-case letters and digits, in words joined by single hyphens or dots",
        text => (TagPattern().IsMatch(text), text));

    // With hyphens as the portals show a GUID, or without as the API writes it. Enclave.Sdk.Api
    // refuses the all-zero GUID when a client is built, after the token check
    // (EnclaveClient.CreateOrganisationClient, CreatePartnerClient), so it is refused here first.
    public static IdFormat<Guid> Guid { get; } = new(
        "a GUID other than all zeros",
        text => (global::System.Guid.TryParseExact(text, "D", out var value) || global::System.Guid.TryParseExact(text, "N", out value))
            && value != global::System.Guid.Empty
            ? (true, value)
            : (false, global::System.Guid.Empty));

    // \z, since $ also matches before a final newline, which would let "web\n" through.
    [GeneratedRegex(@"^([a-z0-9]+[-.])*[a-z0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
