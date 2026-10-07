using System.Globalization;
using Enclave.Api.Modules.SystemManagement.Common.Models;
using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Cli.Commands.Trust;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Configuration.Data.Modules.Policies.Models;
using Enclave.Configuration.Data.Modules.Systems.Enums;
using Enclave.Sdk.Network.NetworkPolicy;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// `policy create &lt;description&gt;`: sends every setting of the policy, with the documented value
/// for each flag left out (proposed-cli-surface.md "Command options", "Create and update").
/// </summary>
internal static class PolicyCreateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("create", "Create a policy, sending every setting, and print it.", CommandScope.Organisation, changes: true);
        var description = verb.Add(CliArguments.Text("description", "The policy's description."));
        var senders = verb.Add(CliOptions.TagList("--senders", "The tags of the systems that send."));
        var receivers = verb.Add(CliOptions.TagList("--receivers", "The tags of the systems that receive."));
        var acls = verb.Add(CliOptions.Labelled("--acl", "Traffic the policy allows: any, icmp, or tcp or udp with a port or range (tcp:5432, udp:8000-8100). At least one is required.", "protocol[:ports]"));
        var trust = verb.Add(CliOptions.List("--trust", "Trust requirements the senders must meet, by description.", "name,..."));
        var trustIds = verb.Add(CliOptions.IdList("--trust-id", "Trust requirements the senders must meet, by ID.", IdFormats.Int32));
        var gateways = verb.Add(CliOptions.Repeated("--gateway", "Make a gateway policy: the senders reach these routes through this system.", "systemId:route,..."));
        var mode = verb.Add(CliOptions.Choice("--mode", "With --gateway: which of several gateways carries a system's traffic; balanced when left out.", PolicyGateways.Modes));
        var subnetFilter = verb.Add(CliOptions.Labelled("--subnet-filter", "With --gateway: narrow the addresses the senders may reach through the gateway.", "range"));
        var activeHours = verb.Add(CliOptions.Text("--active-hours", "Days, times and an IANA time zone when the policy applies: \"mon-fri 08:00-18:00 Europe/London\".", "hours"));
        var forOption = verb.Add(CliOptions.Duration("--for", "Keep the policy for this long."));
        var untilOption = verb.Add(CliOptions.Time("--until", "Keep the policy until this time."));
        var then = verb.Add(CliOptions.Choice("--then", "What happens when the time is up: disable the policy (the default), or delete it.", ("disable", ExpiryAction.Disable), ("delete", ExpiryAction.Delete)));
        var notes = verb.Add(CliOptions.Text("--notes", "The policy's notes."));
        var disabled = verb.Add(CliOptions.Flag("--disabled", "Create the policy disabled."));

        // The API accepts a policy with no ACLs, and the agent then lets no traffic through it
        // (fabric StateTracker.cs:909-921; the API reports it as InactiveNoAcls, portal
        // PolicyModelExtensions.cs:25-29), so the command requires one and always states what
        // traffic the policy allows ("Command options").
        verb.Check(context =>
        {
            if (!context.IsGiven(acls))
            {
                throw CliErrors.InvalidArgument(acls.Name, "Give at least one --acl, the traffic the policy allows; --acl any allows every protocol.");
            }
        });
        verb.Check(context => PolicyAcl.Check(acls.Name, context.Get(acls)));
        verb.Exclusive(trust, trustIds);

        // The senders of a gateway policy reach the gateway's routes, and the API rejects receiver
        // tags on an exit gateway policy (portal PolicyCreateModelValidator.cs:61).
        verb.Check(context =>
        {
            if (context.IsGiven(gateways) && context.IsGiven(receivers))
            {
                throw CliErrors.InvalidArgument(receivers.Name, "--receivers cannot be given with --gateway: the senders of a gateway policy reach the gateway's routes, and a gateway policy has no receivers.");
            }
        });
        verb.Check(context => PolicyGateways.Check(gateways.Name, context.Get(gateways)));

        // The API accepts gateway settings on gateway policies only (portal
        // PolicyCreateModelValidator.cs:47-52).
        verb.Requires(mode, gateways);
        verb.Requires(subnetFilter, gateways);
        verb.Check(context => PolicyActiveHours.Check(activeHours.Name, context.Get(activeHours)));
        verb.Exclusive(forOption, untilOption);
        verb.Requires(then, forOption, untilOption);

        verb.SetHandler(async context =>
        {
            var expiry = context.ExpiryFrom(forOption, untilOption);
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
                AutoExpire = expiry is { } until ? new AutoExpireModel(null, until.ToString("o", CultureInfo.InvariantCulture), context.Get(then) ?? ExpiryAction.Disable) : null,
                ActiveHours = context.Get(activeHours) is { } hours ? PolicyActiveHours.Parse(hours) : null,

                // Exit is the only traffic direction the API accepts (portal
                // PolicyCreateModelValidator.cs:56 rejects Entry). A general policy sends no gateway
                // settings, which the API requires of it (same file, 47-52).
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
