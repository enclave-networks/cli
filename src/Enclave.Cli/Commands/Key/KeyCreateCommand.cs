using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.EnrolmentKeys.Enums;

namespace Enclave.Cli.Commands.Key;

internal static class KeyCreateCommand
{
    // UsesRemaining -1 is no limit on uses (portal EnrolmentKeyCreateModel.UsesRemaining).
    private const int UnlimitedUses = -1;

    // The portal's default for a new ephemeral key (portal-spa appConstants/enrolmentKeys.ts,
    // DEFAULT_RETENTION_MINUTES). Sending it means a change to the API's own default (portal
    // EnrolmentKeyCreateHandler.GetResponseAsync) does not change what the command does ("Create and
    // update").
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);

    public static CliVerb Create()
    {
        var verb = new CliVerb("create", "Create an enrolment key, sending every setting, and print it with its secret.", CommandScope.Organisation, changes: true);
        var description = verb.Add(CliArguments.Name("description", "The key's description.", "a key's description"));
        var ephemeral = verb.Add(CliOptions.Flag("--ephemeral", "Make an ephemeral key: its systems approve automatically, uses are unlimited, and a system is removed after it disconnects."));
        var autoApprove = verb.Add(CliOptions.Flag("--auto-approve", "Approve systems that enrol with the key automatically."));
        var uses = verb.Add(CliOptions.Number("--uses", "How many systems can enrol with the key; unlimited when left out.", minimum: 1));
        var tags = verb.Add(CliOptions.TagList("--tags", "Tags for the systems that enrol with the key.", needsEntries: true));
        var allowIp = verb.Add(CliOptions.Labelled("--allow-ip", "Allow enrolment only from this range.", "range"));
        var keepDisconnected = verb.Add(CliOptions.Duration("--keep-disconnected", "With --ephemeral: keep a system this long after it disconnects; 30m when left out."));
        var timing = Timing.AddTo(verb, "Make the change", "key");
        var notes = verb.Add(CliOptions.Text("--notes", "The key's notes."));

        // The portal sends automatic approval and unlimited uses for an ephemeral key, whatever the
        // form holds (portal-spa createEnrolmentKeySaga.ts).
        Option[] generalPurposeOnly = [autoApprove, uses];
        verb.Check(context =>
        {
            if (context.Get(ephemeral) && generalPurposeOnly.FirstOrDefault(context.IsGiven) is { } option)
            {
                throw CliErrors.InvalidArgument(option.Name, $"An ephemeral key always approves automatically and has unlimited uses, so {option.Name} cannot be given with --ephemeral.");
            }
        });

        // The API takes a retention time on ephemeral keys only (portal EnrolmentKeyCreateValidator,
        // the DisconnectedRetentionMinutes rules).
        verb.Requires(keepDisconnected, ephemeral);

        verb.SetHandler(async context =>
        {
            var isEphemeral = context.Get(ephemeral);

            var model = new EnrolmentKeyCreateModel
            {
                Description = context.Get(description)!,
                Type = isEphemeral ? EnrolmentKeyType.Ephemeral : EnrolmentKeyType.GeneralPurpose,
                ApprovalMode = isEphemeral || context.Get(autoApprove) ? ApprovalMode.Automatic : ApprovalMode.Manual,
                UsesRemaining = context.Get(uses) ?? UnlimitedUses,
                Tags = [.. context.Get(tags) ?? []],
                IpConstraints = [.. LabelledEntry.FromGiven(context.Get(allowIp)).Select(entry => new EnrolmentKeyIpConstraintInputModel(entry.Value, entry.Label))],
                DisconnectedRetentionMinutes = isEphemeral ? KeyRetention.Minutes(context.Get(keepDisconnected) ?? DefaultRetention, keepDisconnected) : null,
                Notes = context.Get(notes),
                AutoExpire = timing.AutoExpire(context),
            };

            var org = await context.GetOrganisationAsync();
            var key = await org.Client.EnrolmentKeys.CreateAsync(model);
            await context.Output.WriteAsync(key, context.CancellationToken);
        });

        return verb;
    }
}
