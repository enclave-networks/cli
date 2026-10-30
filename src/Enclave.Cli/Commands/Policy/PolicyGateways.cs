using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Policies.Models;
using Enclave.Sdk.Network.NetworkPolicy;

namespace Enclave.Cli.Commands.Policy;

internal static class PolicyGateways
{
    // Enclave.Sdk.Api.Data names the API's Ordered value Prioritised, and Enclave.Sdk.Api sends and
    // reads it as Ordered (GatewayPriorityTypeJsonConverter; "`Enclave.Sdk.Api` changes", item 9).
    public static (string Value, GatewayPriorityType Result)[] Modes { get; } =
    [
        ("balanced", GatewayPriorityType.Balanced),
        ("ordered", GatewayPriorityType.Prioritised),
        ("geographic", GatewayPriorityType.Geographic),
    ];

    // Reads one --gateway value, systemId:route,...; null when it is not that form.
    // A system ID holds no ":", so the first one ends it, and an IPv6 route keeps its own. An empty
    // route usually comes from an unset shell variable, so it fails the form, as an empty entry of a
    // comma-list option does (CliOptions.Pieces).
    public static PolicyGateway? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf(':', StringComparison.Ordinal);

        if (split < 0 || !IdFormats.System.TryParse(text[..split], out var systemId))
        {
            return null;
        }

        var routes = text[(split + 1)..].Split(',', StringSplitOptions.TrimEntries);

        return routes.Any(route => route.Length == 0) ? null : new PolicyGateway { SystemId = systemId, Routes = routes };
    }

    // Exits 2 for a value that is not systemId:route,....
    public static void Check(string option, IReadOnlyList<string>? values)
    {
        if (values is not null && values.Any(value => Parse(value) is null))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes a system ID, \":\" and its routes separated by commas, such as GW001:10.0.0.0/16,10.1.0.0/16.");
        }
    }

    public static PolicyGatewayAllowedIpRange[] Ranges(IReadOnlyList<LabelledValue>? given) =>
        LabelledEntry.FromGiven(given).Select(Range).ToArray();

    public static PolicyGatewayAllowedIpRange[] KeepRanges(IReadOnlyList<LabelledValue> given, IEnumerable<PolicyGatewayAllowedIpRange> existing) =>
        LabelledEntry.Keep(given, existing.Select(range => new LabelledEntry(range.IpRange, range.Description))).Select(Range).ToArray();

    private static PolicyGatewayAllowedIpRange Range(LabelledEntry entry) => new() { IpRange = entry.Value, Description = entry.Label };
}
