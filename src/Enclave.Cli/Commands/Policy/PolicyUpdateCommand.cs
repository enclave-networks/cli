using Enclave.Cli.Commands.Trust;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// `policy update`: patches only the settings given (proposed-cli-surface.md "Command options",
/// "Create and update").
/// </summary>
internal static class PolicyUpdateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("update", "Change a policy's settings and print the policy.", CommandScope.Organisation, changes: true);
        var policy = verb.Add(CliArguments.OptionalName("policy", "The policy's description, matched whole and ignoring case.", "a policy's description"));
        var id = verb.Add(CliOptions.Id("--id", "The policy's ID.", IdFormats.Int32));
        // The API refuses a blank description (portal
        // Enclave.Api/Modules/SystemManagement/Policies/Validators/PolicyPatchModelValidator.cs:21,
        // NotEmpty when set) and takes empty notes (same file, 45, MaximumLength only), which clear
        // them.
        var description = verb.Add(CliOptions.NonBlank("--description", "The policy's description.", "a policy's description"));
        var notes = verb.Add(CliOptions.Text("--notes", "The policy's notes."));
        var senders = verb.Add(CliOptions.TagList("--set-senders", "Replace the sender tags."));
        var receivers = verb.Add(CliOptions.TagList("--set-receivers", "Replace the receiver tags."));
        var acls = verb.Add(CliOptions.Labelled("--set-acl", "Replace the traffic the policy allows, keeping the labels of rules that stay.", "protocol[:ports]", clears: true));
        var trust = verb.Add(CliOptions.List("--set-trust", "Replace the trust requirements, by description; \"\" removes them all.", "name,..."));
        var trustIds = verb.Add(CliOptions.IdList("--set-trust-id", "Replace the trust requirements, by ID.", IdFormats.Int32));
        var gateways = verb.Add(CliOptions.Repeated("--set-gateway", "Replace the gateways and their routes.", "systemId:route,..."));
        var mode = verb.Add(CliOptions.Choice("--mode", "Which of several gateways carries a system's traffic.", PolicyGateways.Modes));
        var subnetFilter = verb.Add(CliOptions.Labelled("--set-subnet-filter", "Replace the subnet filter, keeping the labels of ranges that stay.", "range", clears: true));
        var activeHours = verb.Add(CliOptions.Text("--set-active-hours", "Replace the active hours; \"\" removes the restriction.", "hours"));

        verb.ExactlyOne(policy, id);
        verb.AtLeastOne(description, notes, senders, receivers, acls, trust, trustIds, gateways, mode, subnetFilter, activeHours);
        verb.Exclusive(trust, trustIds);

        // The API refuses sender tags, receiver tags or trust requirements that hold a value twice
        // (portal PolicyPatchModelValidator.cs:27-28,42). Trust requirement names match ignoring
        // case ("Names and IDs"), so two that differ only in case name one requirement.
        verb.Check(context =>
        {
            ListValues.CheckNoRepeats(senders.Name, context.Get(senders), StringComparer.Ordinal);
            ListValues.CheckNoRepeats(receivers.Name, context.Get(receivers), StringComparer.Ordinal);
            ListValues.CheckNoRepeats(trust.Name, context.Get(trust), StringComparer.OrdinalIgnoreCase);
            ListValues.CheckNoRepeats(trustIds.Name, context.Get(trustIds));
        });
        verb.Check(context =>
        {
            if (!ListValues.Clears(context.Get(acls)))
            {
                PolicyAcl.Check(acls.Name, context.Get(acls));
            }
        });
        verb.Check(context =>
        {
            if (!ListValues.Clears(context.Get(gateways)))
            {
                PolicyGateways.Check(gateways.Name, context.Get(gateways));
            }
        });
        verb.Check(context =>
        {
            if (context.Get(activeHours) is { Length: > 0 } hours)
            {
                PolicyActiveHours.Check(activeHours.Name, hours);
            }
        });

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var policyId = PolicyId.FromInt(await PolicyLookup.IdAsync(context, org, context.Get(id), context.Get(policy), policy.Name));

            // The patch takes whole lists, so --set-acl and --set-subnet-filter read the policy
            // first, and the entries that stay keep their labels ("Calls per command"). A flag
            // given "" clears its list and has no labels to keep ("Details").
            var aclValues = context.Get(acls);
            var rangeValues = context.Get(subnetFilter);
            var current = (aclValues is not null && !ListValues.Clears(aclValues)) || (rangeValues is not null && !ListValues.Clears(rangeValues))
                ? await SingleItem.CallAsync(() => org.Client.Policies.GetAsync(policyId))
                : null;

            var trustRequirements = context.Get(trustIds)
                ?? (context.Get(trust) is { } names ? await TrustLookup.IdsAsync(context, org, names, trust.Name) : null);

            var patch = org.Client.Policies.Update(policyId);

            if (context.Get(description) is { } newDescription)
            {
                patch.Set(model => model.Description, newDescription);
            }

            if (context.Get(notes) is { } newNotes)
            {
                patch.Set(model => model.Notes, newNotes);
            }

            if (context.Get(senders) is { } senderTags)
            {
                patch.Set(model => model.SenderTags, senderTags.ToArray());
            }

            if (context.Get(receivers) is { } receiverTags)
            {
                patch.Set(model => model.ReceiverTags, receiverTags.ToArray());
            }

            if (aclValues is not null)
            {
                patch.Set(model => model.Acls, ListValues.Clears(aclValues) ? [] : PolicyAcl.Keep(aclValues, current!.Acls));
            }

            if (trustRequirements is not null)
            {
                patch.Set(model => model.SenderTrustRequirements, trustRequirements.Select(TrustRequirementId.FromInt).ToArray());
            }

            if (context.Get(gateways) is { } gatewayValues)
            {
                patch.Set(model => model.Gateways, ListValues.Clears(gatewayValues) ? [] : gatewayValues.Select(gateway => PolicyGateways.Parse(gateway)!).ToArray());
            }

            if (context.Get(mode) is { } priority)
            {
                patch.Set(model => model.GatewayPriority, priority);
            }

            if (rangeValues is not null)
            {
                patch.Set(model => model.GatewayAllowedIpRanges, ListValues.Clears(rangeValues) ? [] : PolicyGateways.KeepRanges(rangeValues, current!.GatewayAllowedIpRanges));
            }

            // "" removes the restriction ("Command options") by patching ActiveHours to null.
            // Enclave.Sdk.Api 1.2.0 sends a null patch value as JSON null, and the API applies every
            // field the body holds, a null one included (Data/PatchClient.cs, Set, citing portal
            // Enclave.Api.Scaffolding PatchModel.WasSet).
            if (context.Get(activeHours) is { } hours)
            {
                patch.Set(model => model.ActiveHours, hours.Length == 0 ? null : PolicyActiveHours.Parse(hours)!);
            }

            // One policy named: a 404 means it does not exist, exit 5 ("Several IDs").
            var updated = await SingleItem.CallAsync(() => patch.ApplyAsync());
            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }
}
