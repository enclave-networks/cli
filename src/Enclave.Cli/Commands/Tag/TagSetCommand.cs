using Enclave.Api.Modules.SystemManagement.Tags.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Tag;

/// <summary>
/// `tag set`: updates a tag, and creates it when it does not exist, which makes it permanent
/// (proposed-cli-surface.md "Command options").
/// </summary>
internal static class TagSetCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("set", "Update a tag, creating it when it does not exist, which makes it permanent; print the tag.", CommandScope.Organisation, changes: true);
        var tag = verb.Add(CliArguments.Id("tag", "The tag's name.", IdFormats.Tag));
        var name = verb.Add(CliOptions.IdText("--name", "Rename the tag; it must exist.", IdFormats.Tag, "new"));
        var colour = verb.Add(CliOptions.Text("--colour", "The tag's colour, such as \"#2f80ed\".", "colour"));
        var trust = verb.Add(CliOptions.List("--trust", "Replace the trust requirements systems with the tag must meet, by description; \"\" removes them all.", "name,..."));
        var trustId = verb.Add(CliOptions.IdList("--trust-id", "Replace the trust requirements systems with the tag must meet, by ID.", IdFormats.Int32));
        var notes = verb.Add(CliOptions.Text("--notes", "The tag's notes."));

        verb.Exclusive(trust, trustId);
        verb.AtLeastOne(name, colour, trust, trustId, notes);

        // The API refuses a tag's trust requirements when they hold one twice (portal
        // TagCreateModelValidator.cs:18, TagPatchModelValidator.cs:20). Trust requirement names
        // match ignoring case ("Names and IDs"), so two that differ only in case name one
        // requirement.
        verb.Check(context =>
        {
            ListValues.CheckNoRepeats(trust.Name, context.Get(trust), StringComparer.OrdinalIgnoreCase);
            ListValues.CheckNoRepeats(trustId.Name, context.Get(trustId));
        });

        verb.SetHandler(async context =>
        {
            var tagName = context.Get(tag)!;
            var org = await context.GetOrganisationAsync();

            var requirements = context.Get(trustId) is { } ids
                ? ids.Select(TrustRequirementId.FromInt).ToArray()
                : await TrustRequirementIdsAsync(context, org, context.Get(trust), trust.Name);

            Task<TagModel> UpdateAsync()
            {
                var patch = org.Client.Tags.Update(tagName);

                if (context.Get(name) is { } newName)
                {
                    patch.Set(model => model.Tag, newName);
                }

                if (context.Get(colour) is { } newColour)
                {
                    patch.Set(model => model.Colour, newColour);
                }

                if (context.Get(notes) is { } newNotes)
                {
                    patch.Set(model => model.Notes, newNotes);
                }

                if (requirements is not null)
                {
                    patch.Set(model => model.TrustRequirements, requirements);
                }

                return patch.ApplyAsync();
            }

            // A create sends an empty list for a list flag left out ("Details").
            Task<TagModel> CreateAsync() => org.Client.Tags.CreateAsync(new TagCreateModel
            {
                Tag = tagName,
                Colour = context.Get(colour),
                Notes = context.Get(notes),
                TrustRequirements = requirements ?? [],
            });

            // --name renames, so the tag must exist: a rename of a tag that does not exist is the
            // single-item not found, exit 5, and creating a tag under either name would not be what
            // was asked.
            void RefuseRenameOfMissingTag(Exception notFound)
            {
                if (context.IsGiven(name))
                {
                    throw ApiErrors.ToCliException(notFound, notFoundIsUnknownItem: true);
                }
            }

            // The API has separate update and create calls, and to a user both mean "make the tag
            // look like this" ("Command options"). The update comes first and is the only call for a
            // tag that exists; the API answers it 404 for a tag it does not hold (portal
            // TagModifyHandler.cs:56-62, GetResponseAsync returns no model, which the API answers as
            // not found), and only then does the create follow ("Calls per command").
            //
            // Under --dry-run the update is captured and answered as a success (DryRun.Answer), so
            // the 404 that leads to the create never comes. The dry run reads the tag instead, and
            // shows the update for a tag that exists and the create for one that does not ("Dry run").
            TagModel result;

            if (context.IsDryRun)
            {
                bool exists;

                try
                {
                    _ = await org.Client.Tags.GetAsync(tagName);
                    exists = true;
                }
                catch (Exception exception) when (ApiErrors.IsNotFound(exception))
                {
                    RefuseRenameOfMissingTag(exception);
                    exists = false;
                }

                result = exists ? await UpdateAsync() : await CreateAsync();
            }
            else
            {
                try
                {
                    result = await UpdateAsync();
                }
                catch (Exception exception) when (ApiErrors.IsNotFound(exception))
                {
                    RefuseRenameOfMissingTag(exception);
                    result = await CreateAsync();
                }
            }

            await context.Output.WriteAsync(result, context.CancellationToken);
        });

        return verb;
    }

    // --trust names trust requirements by description, looked up with one list call ("Names and
    // IDs"). "" gives an empty list, which clears the tag's trust requirements and needs no lookup.
    // Null when --trust is not given.
    private static async Task<TrustRequirementId[]?> TrustRequirementIdsAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string>? descriptions, string key)
    {
        if (descriptions is null)
        {
            return null;
        }

        var found = await NameLookup.FindAsync(
            descriptions,
            (page, perPage) => org.Client.TrustRequirements.GetTrustRequirementsAsync(pageNumber: page, perPage: perPage),
            requirement => requirement.Description,
            requirement => requirement.Id,
            key,
            "trust requirement",
            context.CancellationToken);

        return found.Select(requirement => requirement.Id).Distinct().ToArray();
    }
}
