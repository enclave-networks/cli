using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// `system update`: changes a system's description, notes, tags or gateway routes, or with
/// --pending a waiting system's description, notes or tags, and prints the system the API returns.
/// It sends only the fields given (proposed-cli-surface.md "Create and update").
/// </summary>
internal static class SystemUpdateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("update", "Change a system's description, notes, tags or gateway routes, or with --pending a waiting system's description, notes or tags, and print the system.", CommandScope.Organisation, changes: true);
        var systemId = verb.Add(CliArguments.Id("systemId", "The system's ID.", IdFormats.System));
        var description = verb.Add(CliOptions.Text("--description", "The system's description."));
        var notes = verb.Add(CliOptions.Text("--notes", "The system's notes."));
        var setTags = verb.Add(CliOptions.TagList("--set-tags", "Replace the system's tags; \"\" removes them all."));
        var addTags = verb.Add(CliOptions.TagList("--add-tags", "Add tags, keeping the system's others."));
        var removeTags = verb.Add(CliOptions.TagList("--remove-tags", "Remove tags, keeping the system's others."));
        var enableGatewayFor = verb.Add(CliOptions.Labelled("--enable-gateway-for", "Make the system a gateway for this subnet, replacing the subnets a user entered.", "subnet"));
        var disableGateway = verb.Add(CliOptions.Flag("--disable-gateway", "Stop the system acting as a gateway."));
        var pending = verb.Add(CliOptions.Flag("--pending", "Update a system waiting for approval."));

        verb.Exclusive(enableGatewayFor, disableGateway);

        // A waiting system has no gateway routes to set: UnapprovedSystemPatchModel takes a
        // description, notes and tags only (Enclave.Sdk.Api.Data 304.48.0), so the gateway flags exit
        // 2 with --pending ("Details").
        Option[] enrolledOnly = [enableGatewayFor, disableGateway];
        verb.Check(context =>
        {
            if (context.Get(pending) && enrolledOnly.FirstOrDefault(context.IsGiven) is { } option)
            {
                throw CliErrors.InvalidArgument(option.Name, $"{option.Name} applies to enrolled systems only, so it cannot be given with --pending.");
            }
        });

        verb.AtLeastOne(description, notes, setTags, addTags, removeTags, enableGatewayFor, disableGateway);

        verb.SetHandler(async context =>
        {
            var id = context.Get(systemId)!;
            var change = new Change(
                context.Get(description),
                context.Get(notes),
                context.Get(setTags),
                context.Get(addTags),
                context.Get(removeTags),
                context.Get(enableGatewayFor),
                context.Get(disableGateway));

            var org = await context.GetOrganisationAsync();

            if (context.Get(pending))
            {
                await context.Output.WriteAsync(await UpdateWaitingAsync(org, id, change), context.CancellationToken);
            }
            else
            {
                await context.Output.WriteAsync(await UpdateEnrolledAsync(org, id, change), context.CancellationToken);
            }
        });

        return verb;
    }

    // update names one system, so a 404 on the read or the patch exits 5 ("Several IDs"). The read
    // is made only when the patch needs what the system has: its tags for --add-tags and
    // --remove-tags, and its routes for --enable-gateway-for ("Calls per command").
    private static async Task<SystemModel> UpdateEnrolledAsync(OrganisationInUse org, string id, Change change)
    {
        var current = change.NeedsCurrentTags || change.GatewayFor is not null
            ? await SingleItem.CallAsync(() => org.Client.EnrolledSystems.GetAsync(id))
            : null;

        var patch = org.Client.EnrolledSystems.Update(id);

        if (change.Description is { } description)
        {
            patch.Set(system => system.Description, description);
        }

        if (change.Notes is { } notes)
        {
            patch.Set(system => system.Notes, notes);
        }

        if (change.Tags(current?.Tags.Select(tag => tag.Tag)) is { } tags)
        {
            patch.Set(system => system.Tags, tags);
        }

        if (change.GatewayFor is { } subnets)
        {
            patch.Set<IReadOnlyList<SystemGatewayRouteModel>>(system => system.GatewayRoutes, GatewayRoutes.Replace(subnets, current!.GatewayRoutes));
        }
        else if (change.DisableGateway)
        {
            patch.Set<IReadOnlyList<SystemGatewayRouteModel>>(system => system.GatewayRoutes, []);
        }

        return await SingleItem.CallAsync(patch.ApplyAsync);
    }

    private static async Task<UnapprovedSystemModel> UpdateWaitingAsync(OrganisationInUse org, string id, Change change)
    {
        var current = change.NeedsCurrentTags
            ? await SingleItem.CallAsync(() => org.Client.UnapprovedSystems.GetAsync(id))
            : null;

        var patch = org.Client.UnapprovedSystems.Update(id);

        if (change.Description is { } description)
        {
            patch.Set(system => system.Description, description);
        }

        if (change.Notes is { } notes)
        {
            patch.Set(system => system.Notes, notes);
        }

        if (change.Tags(current?.Tags.Select(tag => tag.Tag)) is { } tags)
        {
            patch.Set(system => system.Tags, tags);
        }

        return await SingleItem.CallAsync(patch.ApplyAsync);
    }

    private sealed record Change(
        string? Description,
        string? Notes,
        IReadOnlyList<string>? SetTags,
        IReadOnlyList<string>? AddTags,
        IReadOnlyList<string>? RemoveTags,
        IReadOnlyList<LabelledValue>? GatewayFor,
        bool DisableGateway)
    {
        public bool NeedsCurrentTags => TagEdits.NeedsCurrentTags(SetTags, AddTags, RemoveTags);

        // The patch models take the whole tag list as names (SystemPatchModel.Tags,
        // UnapprovedSystemPatchModel.Tags), where the models read hold tag objects.
        public string[]? Tags(IEnumerable<string>? current) =>
            TagEdits.Apply(SetTags, AddTags, RemoveTags, current)?.ToArray();
    }
}
