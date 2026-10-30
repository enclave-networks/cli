using Enclave.Api.Modules.SystemManagement.Tags.Models;
using Enclave.Cli.Commands.Trust;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Tag;

internal static class TagSetCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("set", "Update a tag, creating it when it does not exist, which makes it permanent; print the tag.", CommandScope.Organisation, changes: true);
        var tag = verb.Add(CliArguments.Id("tag", "The tag's name.", IdFormats.Tag));
        var name = verb.Add(CliOptions.IdText("--name", "Rename the tag; it must exist.", IdFormats.Tag, "new"));
        // The API takes a colour as a hex code with "#", or none, so it refuses a blank one (portal
        // TagValidationExtensions.MustBeValidHexColour).
        var colour = verb.Add(CliOptions.NonBlank("--colour", "The tag's colour, such as \"#2f80ed\".", "a colour", "colour"));
        var trust = verb.Add(CliOptions.List("--trust", "Replace the trust requirements systems with the tag must meet, by description; \"\" removes them all.", "name,..."));
        var trustId = verb.Add(CliOptions.IdList("--trust-id", "Replace the trust requirements systems with the tag must meet, by ID.", IdFormats.Int32));
        var notes = verb.Add(CliOptions.Text("--notes", "The tag's notes."));

        verb.Exclusive(trust, trustId);
        verb.AtLeastOne(name, colour, trust, trustId, notes);

        // The API refuses a tag's trust requirements when they hold one twice (portal
        // TagCreateModelValidator, TagPatchModelValidator); trust requirement names match ignoring
        // case ("Names and IDs").
        verb.Check(context =>
        {
            ListValues.CheckNoRepeats(trust.Name, context.Get(trust), StringComparer.OrdinalIgnoreCase);
            ListValues.CheckNoRepeats(trustId.Name, context.Get(trustId));
        });

        verb.SetHandler(async context =>
        {
            var tagName = context.Get(tag)!;
            var org = await context.GetOrganisationAsync();

            // --trust "" gives an empty list, which clears the tag's trust requirements.
            var requirements = context.Get(trustId) is { } ids
                ? ids.Select(TrustRequirementId.FromInt).ToArray()
                : context.Get(trust) is { } descriptions
                    ? (await TrustLookup.IdsAsync(context, org, descriptions, trust.Name)).Distinct().Select(TrustRequirementId.FromInt).ToArray()
                    : null;

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

            // A rename of a tag that does not exist exits 5: creating a tag under either name would
            // not be what was asked.
            void RefuseRenameOfMissingTag(Exception notFound)
            {
                if (context.IsGiven(name))
                {
                    throw ApiErrors.ToCliException(notFound, notFoundIsUnknownItem: true);
                }
            }

            // The update comes first and is the only call for a tag that exists; the API answers it
            // 404 for a tag it does not hold (portal TagModifyHandler.GetResponseAsync), and the
            // create follows ("Calls per command"). A dry run answers the update as a success
            // (DryRun.Answer), so it reads the tag to choose instead ("Dry run").
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
}
