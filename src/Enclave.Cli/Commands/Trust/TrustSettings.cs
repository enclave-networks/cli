using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Trust;

/// <summary>
/// The settings of a trust requirement as the API stores them: a configuration and a list of
/// conditions, every value a string (portal TrustRequirementSettingsModel). A sign-in requirement's
/// configuration names its authority and that authority's settings, and its conditions are claims;
/// a public IP requirement's conditions are IP ranges and countries, allowed or blocked.
/// </summary>
internal static class TrustSettings
{
    // The configuration keys and authority names the API reads (portal
    // Enclave.PolicyEngine.Abstractions/TrustRequirements/UserAuthenticationConstants.cs).
    public const string AuthorityKey = "authority";

    public const string TenantKey = "tenantId";

    public const string AuthorityUriKey = "authorityUri";

    public const string ClientIdKey = "clientId";

    public const string AudienceKey = "aud";

    // The keys of a public IP condition and the values of its type (services
    // Enclave.Services.Common/TrustRequirements/PublicIpConstants.cs; portal
    // TrustRequirementSettingsPublicIpValidator.cs:27 allows these keys and no others).
    private const string TypeKey = "type";

    private const string ValueKey = "value";

    private const string IsBlockedKey = "isBlocked";

    private const string DescriptionKey = "description";

    private const string IpType = "ip";

    private const string CountryType = "country";

    private static readonly string[] GenericAuthorities = ["okta", "jumpcloud", "duo", "oidc"];

    private static readonly string[] GenericKeys = [AuthorityUriKey, ClientIdKey, AudienceKey];

    private static readonly string[] AzureKeys = [TenantKey];

    /// <summary>
    /// The authorities --authority takes, each the name the API reads.
    /// </summary>
    public static (string Value, string Result)[] Authorities { get; } =
    [
        ("portal", "portal"),
        ("azure", "azure"),
        ("google", "google"),
        ("okta", "okta"),
        ("jumpcloud", "jumpcloud"),
        ("duo", "duo"),
        ("oidc", "oidc"),
    ];

    /// <summary>
    /// An allowed IP range.
    /// </summary>
    public static PublicIpKind AllowedIp { get; } = new(IpType, Blocked: false);

    /// <summary>
    /// A blocked IP range.
    /// </summary>
    public static PublicIpKind BlockedIp { get; } = new(IpType, Blocked: true);

    /// <summary>
    /// An allowed country.
    /// </summary>
    public static PublicIpKind AllowedCountry { get; } = new(CountryType, Blocked: false);

    /// <summary>
    /// A blocked country.
    /// </summary>
    public static PublicIpKind BlockedCountry { get; } = new(CountryType, Blocked: true);

    /// <summary>
    /// The configuration keys an authority takes besides its name. Each authority takes only its
    /// own settings, as the API does (portal TrustRequirementSettingsUserAuthValidator.cs:34-48):
    /// azure takes a tenant, portal and google take none, and okta, jumpcloud, duo and oidc take an
    /// address, a client ID and an audience.
    /// </summary>
    public static IReadOnlyList<string> KeysOf(string? authority) =>
        authority == "azure" ? AzureKeys : IsGeneric(authority) ? GenericKeys : [];

    /// <summary>
    /// Whether the authority requires this key: okta, jumpcloud, duo and oidc require their https
    /// address and a client ID (portal TrustRequirementSettingsUserAuthValidator.cs:47-48).
    /// </summary>
    public static bool Requires(string? authority, string key) =>
        IsGeneric(authority) && key is AuthorityUriKey or ClientIdKey;

    /// <summary>
    /// Whether the text is an absolute https address, which the API requires of authorityUri
    /// (portal TrustRequirementSettingsUserAuthValidator.cs:47, KeyMustBeHttpsUri).
    /// </summary>
    public static bool IsHttpsUri(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Checks each value of a claim option, claim=value, exiting 2 for one without "=" or a claim
    /// name.
    /// </summary>
    public static void CheckClaims(string option, IReadOnlyList<string>? values)
    {
        if (values is not null && values.Any(value => value.IndexOf('=', StringComparison.Ordinal) < 1))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes a claim, \"=\" and the value the sign-in token must carry, such as groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84.");
        }
    }

    /// <summary>
    /// A claim condition, { claim, value }, from claim=value, split at the first "=" (portal
    /// TrustRequirementSettingsUserAuthValidator.cs:55 allows these keys and no others).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Claim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf('=', StringComparison.Ordinal);
        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["claim"] = text[..split], ["value"] = text[(split + 1)..] };
    }

    /// <summary>
    /// A public IP condition of this kind for a range or country and its label. isBlocked is sent
    /// on every condition, since the check skips a condition without it (services
    /// Enclave.Discover/TrustValidators/PublicIpValidator.cs:163-167), as the string the API stores.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Condition(PublicIpKind kind, LabelledEntry entry)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(entry);

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TypeKey] = kind.Type,
            [ValueKey] = entry.Value,
            [IsBlockedKey] = kind.Blocked ? "true" : "false",
            [DescriptionKey] = entry.Label,
        };
    }

    /// <summary>
    /// The conditions of a public IP requirement with those of one kind replaced by the values
    /// given, each keeping the label of the condition of that kind with the same value unless the
    /// flag gives one, and the conditions of other kinds left as they are (proposed-cli-surface.md
    /// "Command options", trust update). Countries compare ignoring case.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> Replace(
        IReadOnlyList<IReadOnlyDictionary<string, string?>> conditions,
        PublicIpKind kind,
        IReadOnlyList<LabelledValue> given)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(given);

        var existing = conditions
            .Where(condition => KindOf(condition) == kind)
            .Select(condition => new LabelledEntry(condition.GetValueOrDefault(ValueKey) ?? string.Empty, condition.GetValueOrDefault(DescriptionKey)));

        var comparer = kind.Type == CountryType ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var replaced = LabelledEntry.Keep(given, existing, comparer).Select(entry => Condition(kind, entry));

        return conditions.Where(condition => KindOf(condition) != kind).Concat(replaced).ToArray();
    }

    /// <summary>
    /// The kind of a public IP condition, or null for one the check reads as neither allowed nor
    /// blocked: the check skips a condition without a readable isBlocked (services
    /// Enclave.Discover/TrustValidators/PublicIpValidator.cs:163-167, bool.Parse).
    /// </summary>
    private static PublicIpKind? KindOf(IReadOnlyDictionary<string, string?> condition) =>
        condition.GetValueOrDefault(TypeKey) is { } type && bool.TryParse(condition.GetValueOrDefault(IsBlockedKey), out var blocked)
            ? new PublicIpKind(type, blocked)
            : null;

    private static bool IsGeneric(string? authority) =>
        authority is not null && GenericAuthorities.Contains(authority, StringComparer.Ordinal);

    /// <summary>
    /// One kind of public IP condition: an IP range or a country, allowed or blocked.
    /// </summary>
    /// <param name="Type">The condition's type: ip or country.</param>
    /// <param name="Blocked">Whether the condition blocks addresses it matches.</param>
    internal sealed record PublicIpKind(string Type, bool Blocked);
}
