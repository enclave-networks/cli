using Enclave.Api.Modules.SystemManagement.EnrolmentKeys;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.EnrolmentKeys.Enums;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// `key update`: changes the settings given, leaves the rest as they are, and prints the key
/// (proposed-cli-surface.md "Command options", "Create and update").
/// </summary>
internal static class KeyUpdateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("update", "Change a key's settings and print the key.", CommandScope.Organisation, changes: true);
        var key = verb.Add(CliArguments.OptionalText("key", "The key's description, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The key's ID.", IdFormats.Int32));
        var description = verb.Add(CliOptions.Text("--description", "The key's description."));
        var notes = verb.Add(CliOptions.Text("--notes", "The key's notes."));
        var autoApprove = verb.Add(CliOptions.Flag("--auto-approve", "Approve systems that enrol with the key automatically."));
        var requireApproval = verb.Add(CliOptions.Flag("--require-approval", "Require approval for systems that enrol with the key."));
        var uses = verb.Add(CliOptions.Number("--uses", "How many more systems can enrol with the key.", minimum: 1));
        var setTags = verb.Add(CliOptions.TagList("--set-tags", "Replace the key's tags; \"\" removes them all."));
        var addTags = verb.Add(CliOptions.TagList("--add-tags", "Add tags, keeping the key's others."));
        var removeTags = verb.Add(CliOptions.TagList("--remove-tags", "Remove tags, keeping the key's others."));
        var setAllowIp = verb.Add(CliOptions.Labelled("--set-allow-ip", "Replace the ranges enrolment is allowed from, keeping the labels of ranges that stay.", "range"));
        var keepDisconnected = verb.Add(CliOptions.Duration("--keep-disconnected", "Keep a system of an ephemeral key this long after it disconnects."));

        verb.ExactlyOne(key, id);
        verb.Exclusive(autoApprove, requireApproval);
        verb.AtLeastOne(description, notes, autoApprove, requireApproval, uses, setTags, addTags, removeTags, setAllowIp, keepDisconnected);

        verb.SetHandler(async context =>
        {
            int? retention = context.Get(keepDisconnected) is { } duration ? KeyRetention.Minutes(duration, keepDisconnected) : null;

            var org = await context.GetOrganisationAsync();
            var keyId = await KeyLookup.IdAsync(context, org, key, id);

            var set = context.Get(setTags);
            var add = context.Get(addTags);
            var remove = context.Get(removeTags);
            var allow = context.Get(setAllowIp);

            // The patch takes whole lists, so --add-tags and --remove-tags read the key to keep the
            // tags it has, and --set-allow-ip reads it to keep the labels of ranges that stay
            // ("Create and update", "Calls per command").
            var current = TagEdits.NeedsCurrentTags(set, add, remove) || allow is not null
                ? await SingleItem.CallAsync(() => org.Client.EnrolmentKeys.GetAsync(keyId))
                : null;

            var patch = org.Client.EnrolmentKeys.Update(keyId);

            if (context.Get(description) is { } newDescription)
            {
                patch.Set(model => model.Description, newDescription);
            }

            if (context.Get(notes) is { } newNotes)
            {
                patch.Set(model => model.Notes, newNotes);
            }

            if (context.Get(autoApprove) || context.Get(requireApproval))
            {
                patch.Set(model => model.ApprovalMode, context.Get(autoApprove) ? ApprovalMode.Automatic : ApprovalMode.Manual);
            }

            if (context.Get(uses) is { } newUses)
            {
                patch.Set(model => model.UsesRemaining, newUses);
            }

            if (TagEdits.Apply(set, add, remove, current?.Tags.Select(tag => tag.Tag)) is { } tags)
            {
                patch.Set(model => model.Tags, [.. tags]);
            }

            if (allow is not null)
            {
                var existing = (current?.IpConstraints ?? []).Select(constraint => new LabelledEntry(constraint.Range, constraint.Description));
                patch.Set(model => model.IpConstraints, [.. LabelledEntry.Keep(allow, existing).Select(entry => new EnrolmentKeyIpConstraintInputModel(entry.Value, entry.Label))]);
            }

            if (retention is { } minutes)
            {
                patch.Set(model => model.DisconnectedRetentionMinutes, minutes);
            }

            var updated = await SingleItem.CallAsync(() => patch.ApplyAsync());
            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }
}
