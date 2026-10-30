using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Trust;

// A trust requirement's settings are a configuration and a list of conditions, every value a
// string (portal TrustRequirementSettingsModel). A sign-in requirement's conditions are claims; a
// public IP requirement's are ranges and countries, allowed or blocked.
internal static class TrustSettings
{
    // The configuration keys the API reads (portal UserAuthenticationConstants).
    public const string AuthorityKey = "authority";

    public const string TenantKey = "tenantId";

    public const string AuthorityUriKey = "authorityUri";

    public const string ClientIdKey = "clientId";

    public const string AudienceKey = "aud";

    // The keys and types of a public IP condition (services PublicIpConstants). The API allows no
    // other keys (portal TrustRequirementSettingsPublicIpValidator).
    private const string TypeKey = "type";

    private const string ValueKey = "value";

    private const string IsBlockedKey = "isBlocked";

    private const string DescriptionKey = "description";

    private const string IpType = "ip";

    private const string CountryType = "country";

    private static readonly string[] GenericAuthorities = ["okta", "jumpcloud", "duo", "oidc"];

    private static readonly string[] GenericKeys = [AuthorityUriKey, ClientIdKey, AudienceKey];

    private static readonly string[] AzureKeys = [TenantKey];

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

    public static PublicIpKind AllowedIp { get; } = new(IpType, Blocked: false);

    public static PublicIpKind BlockedIp { get; } = new(IpType, Blocked: true);

    public static PublicIpKind AllowedCountry { get; } = new(CountryType, Blocked: false);

    public static PublicIpKind BlockedCountry { get; } = new(CountryType, Blocked: true);

    // The API refuses other keys only for azure and portal (portal
    // TrustRequirementSettingsUserAuthValidator). The CLI refuses them for every authority, so a
    // setting given for the wrong authority exits 2 before it is stored.
    public static IReadOnlyList<string> KeysOf(string? authority) =>
        authority == "azure" ? AzureKeys : IsGeneric(authority) ? GenericKeys : [];

    // As the API requires (portal TrustRequirementSettingsUserAuthValidator).
    public static bool Requires(string? authority, string key) =>
        IsGeneric(authority) && key is AuthorityUriKey or ClientIdKey;

    // The API's test of authorityUri (portal DictionaryValidationExtensions.KeyMustBeHttpsUri).
    public static bool IsHttpsUri(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Exits 2 for a value without "=" or a claim name.
    /// </summary>
    public static void CheckClaims(string option, IReadOnlyList<string>? values)
    {
        if (values is not null && values.Any(value => value.IndexOf('=', StringComparison.Ordinal) < 1))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes a claim, \"=\" and the value the sign-in token must carry, such as groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84.");
        }
    }

    // Split at the first "=", so the value can hold one. The API allows these keys and no others
    // (portal TrustRequirementSettingsUserAuthValidator).
    public static IReadOnlyDictionary<string, string?> Claim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf('=', StringComparison.Ordinal);
        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["claim"] = text[..split], ["value"] = text[(split + 1)..] };
    }

    // isBlocked goes on every condition, since the check skips a condition without it (services
    // PublicIpValidator.GetIpRanges, GetCountries).
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
    /// Replaces the conditions of one kind with the values given. A value that stays keeps its
    /// label unless it gives one; countries compare ignoring case. Conditions of other kinds stay
    /// as they are.
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

    // Null when isBlocked is missing or not a bool, which the check does not read as allowed or
    // blocked (services PublicIpValidator.GetIpRanges, GetCountries).
    private static PublicIpKind? KindOf(IReadOnlyDictionary<string, string?> condition) =>
        condition.GetValueOrDefault(TypeKey) is { } type && bool.TryParse(condition.GetValueOrDefault(IsBlockedKey), out var blocked)
            ? new PublicIpKind(type, blocked)
            : null;

    private static bool IsGeneric(string? authority) =>
        authority is not null && GenericAuthorities.Contains(authority, StringComparer.Ordinal);

    internal sealed record PublicIpKind(string Type, bool Blocked);
}
