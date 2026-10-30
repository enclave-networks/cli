using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Cli.Commands.Trust;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Configuration.Data.Modules.Policies.Models;
using Enclave.Configuration.Data.Modules.Systems.Enums;
using Enclave.Sdk.Network.NetworkPolicy;

namespace Enclave.Cli.Commands.Policy;

internal static class PolicyCreateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("create", "Create a policy, sending every setting, and print it.", CommandScope.Organisation, changes: true);
        var description = verb.Add(CliArguments.Name("description", "The policy's description.", "a policy's description"));
        var senders = verb.Add(CliOptions.TagList("--senders", "The tags of the systems that send.", needsEntries: true));
        var receivers = verb.Add(CliOptions.TagList("--receivers", "The tags of the systems that receive.", needsEntries: true));
        var acls = verb.Add(CliOptions.Labelled("--acl", "Traffic the policy allows: any, icmp, or tcp or udp with a port or range (tcp:5432, udp:8000-8100). At least one is required.", "protocol[:ports]"));
        var trust = verb.Add(CliOptions.List("--trust", "Trust requirements the senders must meet, by description.", "name,...", needsEntries: true));
        var trustIds = verb.Add(CliOptions.IdList("--trust-id", "Trust requirements the senders must meet, by ID.", IdFormats.Int32, needsEntries: true));
        var gateways = verb.Add(CliOptions.Repeated("--gateway", "Make a gateway policy: the senders reach these routes through this system.", "systemId:route,..."));
        var mode = verb.Add(CliOptions.Choice("--mode", "With --gateway: which of several gateways carries a system's traffic; balanced when left out.", PolicyGateways.Modes));
        var subnetFilter = verb.Add(CliOptions.Labelled("--subnet-filter", "With --gateway: narrow the addresses the senders may reach through the gateway.", "range"));
        var activeHours = verb.Add(CliOptions.Text("--active-hours", "Days, times and an IANA time zone when the policy applies: \"mon-fri 08:00-18:00 Europe/London\".", "hours"));
        var timing = Timing.AddTo(verb, "Keep the policy", "policy");
        var notes = verb.Add(CliOptions.Text("--notes", "The policy's notes."));
        var disabled = verb.Add(CliOptions.Flag("--disabled", "Create the policy disabled."));

        // The API accepts a policy with no ACLs, and the agent then lets no traffic through it
        // (fabric StateTracker.DoesPolicyAllowTraffic; portal PolicyModelExtensions.ToModel reports
        // it InactiveNoAcls).
        verb.Check(context =>
        {
            if (!context.IsGiven(acls))
            {
                throw CliErrors.InvalidArgument(acls.Name, "Give at least one --acl, the traffic the policy allows; --acl any allows every protocol.");
            }
        });
        verb.Check(context => PolicyAcl.Check(acls.Name, context.Get(acls)));
        verb.Exclusive(trust, trustIds);

        // The API refuses sender tags, receiver tags or trust requirements that hold a value twice
        // (portal PolicyCreateModelValidator, MustNotHaveDuplicates); trust requirement names match
        // ignoring case ("Names and IDs"). It stores a repeated ACL, gateway or subnet filter, so
        // those are sent as given.
        verb.Check(context =>
        {
            ListValues.CheckNoRepeats(senders.Name, context.Get(senders), StringComparer.Ordinal);
            ListValues.CheckNoRepeats(receivers.Name, context.Get(receivers), StringComparer.Ordinal);
            ListValues.CheckNoRepeats(trust.Name, context.Get(trust), StringComparer.OrdinalIgnoreCase);
            ListValues.CheckNoRepeats(trustIds.Name, context.Get(trustIds));
        });

        // The API refuses receiver tags on an exit gateway policy (portal PolicyCreateModelValidator,
        // the ReceiverTags rules).
        verb.Check(context =>
        {
            if (context.IsGiven(gateways) && context.IsGiven(receivers))
            {
                throw CliErrors.InvalidArgument(receivers.Name, "--receivers cannot be given with --gateway: the senders of a gateway policy reach the gateway's routes, and a gateway policy has no receivers.");
            }
        });
        verb.Check(context => PolicyGateways.Check(gateways.Name, context.Get(gateways)));

        // The API refuses a subnet filter on a general policy (portal PolicyCreateModelValidator). It
        // accepts a mode there but drops it, since a general policy has no gateways to order
        // (PolicyModelExtensions.ToModel).
        verb.Requires(mode, gateways);
        verb.Requires(subnetFilter, gateways);
        verb.Check(context => PolicyActiveHours.Check(activeHours.Name, context.Get(activeHours)));

        verb.SetHandler(async context =>
        {
            var autoExpire = timing.AutoExpire(context);
            var gatewayList = context.Get(gateways)?.Select(gateway => PolicyGateways.Parse(gateway)!).ToArray();
            GatewayPriorityType? priority = gatewayList is null ? null : context.Get(mode) ?? GatewayPriorityType.Balanced;

            var org = await context.GetOrganisationAsync();
            var trustRequirements = context.Get(trustIds) ?? await TrustLookup.IdsAsync(context, org, context.Get(trust) ?? [], trust.Name);

            var model = new PolicyCreateModel
            {
                Type = gatewayList is null ? PolicyType.General : PolicyType.Gateway,
                Description = context.Get(description)!,
                IsEnabled = !context.Get(disabled),
                SenderTags = [.. context.Get(senders) ?? []],
                ReceiverTags = [.. context.Get(receivers) ?? []],
                Acls = PolicyAcl.FromGiven(context.Get(acls)!),
                SenderTrustRequirements = [.. trustRequirements.Select(TrustRequirementId.FromInt)],
                Notes = context.Get(notes),
                AutoExpire = autoExpire,
                ActiveHours = context.Get(activeHours) is { } hours ? PolicyActiveHours.Parse(hours) : null,

                // The API refuses Entry, and refuses gateway settings on a general policy (portal
                // PolicyCreateModelValidator).
                Gateways = gatewayList ?? [],
                GatewayTrafficDirection = gatewayList is null ? null : GatewayTrafficDirection.Exit,
                GatewayPriority = priority,
                GatewayAllowedIpRanges = PolicyGateways.Ranges(context.Get(subnetFilter)),
            };

            var policy = await org.Client.Policies.CreateAsync(model);
            await context.Output.WriteAsync(policy, context.CancellationToken);
        });

        return verb;
    }
}
