using System.Globalization;
using System.Text.RegularExpressions;
using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Cli.Core;
using Enclave.Sdk.Network.NetworkPolicy;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// One ACL rule: a protocol and, for TCP and UDP, a port or range (proposed-cli-surface.md "Command
/// options", --acl).
/// </summary>
/// <param name="Protocol">The protocol.</param>
/// <param name="Start">The first port, or null for any and icmp.</param>
/// <param name="End">The last port, the same as <paramref name="Start"/> for one port.</param>
internal sealed partial record PolicyAcl(PolicyAclProtocol Protocol, int? Start, int? End)
{
    private const int MaxPort = 65535;

    private static readonly (string Name, PolicyAclProtocol Protocol, bool TakesPorts)[] Protocols =
    [
        ("any", PolicyAclProtocol.Any, false),
        ("tcp", PolicyAclProtocol.Tcp, true),
        ("udp", PolicyAclProtocol.Udp, true),
        ("icmp", PolicyAclProtocol.Icmp, false),
    ];

    /// <summary>
    /// The ports as the API takes them: "5432", "8000-8100", or null for any and icmp.
    /// </summary>
    public string? Ports => Start is not { } start
        ? null
        : start == End ? start.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{start}-{End}");

    /// <summary>
    /// The rule as --acl writes it, lower case: tcp:8000-8100, icmp. Two rules that allow the same
    /// traffic have the same text.
    /// </summary>
    public string Text => Ports is { } ports ? $"{Name(Protocol)}:{ports}" : Name(Protocol);

    /// <summary>
    /// Reads --acl's value, without its label: any, icmp, or tcp or udp with a port from 1 to 65535
    /// or a range low-high with low no greater than high ("Details"). The protocol matches ignoring
    /// case. Null for anything else.
    /// </summary>
    public static PolicyAcl? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.IndexOf(':', StringComparison.Ordinal);
        var name = split < 0 ? text : text[..split];
        var match = Protocols.Where(protocol => string.Equals(protocol.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (match.Length != 1)
        {
            return null;
        }

        var (_, protocol, takesPorts) = match[0];

        // Any and ICMP carry every port, and TCP and UDP need a port or range (portal
        // PolicyAclModelValidator.cs).
        if (!takesPorts)
        {
            return split < 0 ? new PolicyAcl(protocol, null, null) : null;
        }

        if (split < 0)
        {
            return null;
        }

        var ports = CliPorts().Match(text[(split + 1)..]);

        if (!ports.Success || !TryPort(ports.Groups[1].Value, out var start))
        {
            return null;
        }

        var end = start;

        if (ports.Groups[2].Success && !TryPort(ports.Groups[2].Value, out end))
        {
            return null;
        }

        return start <= end ? new PolicyAcl(protocol, start, end) : null;
    }

    /// <summary>
    /// The rule an ACL the API returned allows. The API writes a range with spaces, "8000 - 8100"
    /// (portal PolicyModelExtensions.FormatPorts), and reads it with or without them (portal
    /// AclConversionExtensions.TryParsePorts), so both forms give the same rule. Null when its ports
    /// cannot be read.
    /// </summary>
    public static PolicyAcl? FromModel(PolicyAclModel acl)
    {
        ArgumentNullException.ThrowIfNull(acl);

        if (string.IsNullOrEmpty(acl.Ports))
        {
            return new PolicyAcl(acl.Protocol, null, null);
        }

        var ports = ApiPorts().Match(acl.Ports);

        if (!ports.Success || !int.TryParse(ports.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return null;
        }

        var end = start;

        if (ports.Groups[2].Success && !int.TryParse(ports.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out end))
        {
            return null;
        }

        return new PolicyAcl(acl.Protocol, start, end);
    }

    /// <summary>
    /// The ACLs a create sends: each --acl with its label as the description.
    /// </summary>
    public static PolicyAclModel[] FromGiven(IReadOnlyList<LabelledValue> given)
    {
        ArgumentNullException.ThrowIfNull(given);

        return given.Select(value => Parse(value.Value)!.ToModel(value.Label)).ToArray();
    }

    /// <summary>
    /// The ACLs an update sends for --set-acl: each rule given, keeping the label of the rule the
    /// policy has that allows the same traffic, unless the flag gives one ("Command options").
    /// </summary>
    public static PolicyAclModel[] Keep(IReadOnlyList<LabelledValue> given, IEnumerable<PolicyAclModel> existing)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(existing);

        var rules = given.Select(value => Parse(value.Value)!).ToArray();
        var kept = LabelledEntry.Keep(
            given.Zip(rules, (value, rule) => value with { Value = rule.Text }).ToArray(),
            existing.Select(acl => FromModel(acl) is { } rule ? new LabelledEntry(rule.Text, acl.Description) : null).OfType<LabelledEntry>());

        return rules.Zip(kept, (rule, entry) => rule.ToModel(entry.Label)).ToArray();
    }

    /// <summary>
    /// Checks each value of an ACL option, exiting 2 for one that is not a rule.
    /// </summary>
    public static void Check(string option, IReadOnlyList<LabelledValue>? values)
    {
        if (values is not null && values.Any(value => Parse(value.Value) is null))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes any, icmp, or tcp or udp with a port from 1 to 65535 or a range low-high (tcp:5432, udp:8000-8100), with an optional =label.");
        }
    }

    private PolicyAclModel ToModel(string? label) => new(Protocol, Ports, label);

    private static string Name(PolicyAclProtocol protocol) => Protocols.First(entry => entry.Protocol == protocol).Name;

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= MaxPort;

    [GeneratedRegex(@"^([0-9]{1,5})(?:-([0-9]{1,5}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex CliPorts();

    [GeneratedRegex(@"^\s*([0-9]{1,5})\s*(?:-\s*([0-9]{1,5}))?\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ApiPorts();
}
