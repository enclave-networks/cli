using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Policies.Models;
using Enclave.Sdk.Network.NetworkPolicy;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// The gateway settings of a policy: --gateway, --mode and --subnet-filter (proposed-cli-surface.md
/// "Command options").
/// </summary>
internal static class PolicyGateways
{
    /// <summary>
    /// --mode's values, each standing for the API's GatewayPriority value (sdk
    /// Enclave.Sdk.Network/NetworkPolicy/GatewayPriorityType.cs, which the API uses).
    /// </summary>
    // Enclave.Sdk.Api.Data 304.48.0 names the API's Ordered value Prioritised (portal
    // Enclave.Sdk.Api.Data/Duplicated/GatewayPriorityType.cs). Enclave.Sdk.Api 1.1.0 sends and reads
    // Prioritised as Ordered (Data/GatewayPriorityTypeJsonConverter.cs, which every client uses), so
    // ordered stands for Prioritised ("`Enclave.Sdk.Api` changes", item 9).
    public static (string Value, GatewayPriorityType Result)[] Modes { get; } =
    [
        ("balanced", GatewayPriorityType.Balanced),
        ("ordered", GatewayPriorityType.Prioritised),
        ("geographic", GatewayPriorityType.Geographic),
    ];

    /// <summary>
    /// Reads one --gateway value, systemId:route,route; null when it is not that form or the system
    /// ID is not letters and digits. A system ID holds no ":", so the first one ends it, and an IPv6
    /// route keeps its own.
    /// </summary>
    public static PolicyGateway? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf(':', StringComparison.Ordinal);

        if (split < 0 || !IdFormats.System.TryParse(text[..split], out var systemId))
        {
            return null;
        }

        var routes = text[(split + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return routes.Length == 0 ? null : new PolicyGateway { SystemId = systemId, Routes = routes };
    }

    /// <summary>
    /// Checks each value of a gateway option, exiting 2 for one that is not systemId:route,....
    /// </summary>
    public static void Check(string option, IReadOnlyList<string>? values)
    {
        if (values is not null && values.Any(value => Parse(value) is null))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes a system ID, \":\" and its routes separated by commas, such as GW001:10.0.0.0/16,10.1.0.0/16.");
        }
    }

    /// <summary>
    /// The subnet filter a create sends: each range with its label as the description.
    /// </summary>
    public static PolicyGatewayAllowedIpRange[] Ranges(IReadOnlyList<LabelledValue>? given) =>
        LabelledEntry.FromGiven(given).Select(Range).ToArray();

    /// <summary>
    /// The subnet filter an update sends for --set-subnet-filter: each range given, keeping the
    /// label of the range the policy has, unless the flag gives one ("Command options").
    /// </summary>
    public static PolicyGatewayAllowedIpRange[] KeepRanges(IReadOnlyList<LabelledValue> given, IEnumerable<PolicyGatewayAllowedIpRange> existing) =>
        LabelledEntry.Keep(given, existing.Select(range => new LabelledEntry(range.IpRange, range.Description))).Select(Range).ToArray();

    private static PolicyGatewayAllowedIpRange Range(LabelledEntry entry) => new() { IpRange = entry.Value, Description = entry.Label };
}
