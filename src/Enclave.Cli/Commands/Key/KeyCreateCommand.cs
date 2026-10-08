using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.EnrolmentKeys.Enums;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// `key create`: creates an enrolment key, sending every setting, and prints it with its secret
/// (proposed-cli-surface.md "Command options", "Create and update").
/// </summary>
internal static class KeyCreateCommand
{
    // UsesRemaining -1 is no limit on uses (portal EnrolmentKeyCreateModel.UsesRemaining).
    private const int UnlimitedUses = -1;

    // The portal's retention for a new ephemeral key (portal-spa appConstants/enrolmentKeys.ts:14,
    // DEFAULT_RETENTION_MINUTES, set in EnrolmentKeys/Details/Blocks/Purpose/Create.tsx:54-59). The
    // CLI sends it, where the API applies a default of its own when none is sent (portal
    // EnrolmentKeyCreateHandler.cs:67-74), so a change to the API's default does not change what the
    // command does ("Create and update").
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);

    public static CliVerb Create()
    {
        var verb = new CliVerb("create", "Create an enrolment key, sending every setting, and print it with its secret.", CommandScope.Organisation, changes: true);
        var description = verb.Add(CliArguments.Text("description", "The key's description."));
        var ephemeral = verb.Add(CliOptions.Flag("--ephemeral", "Make an ephemeral key: its systems approve automatically, uses are unlimited, and a system is removed after it disconnects."));
        var autoApprove = verb.Add(CliOptions.Flag("--auto-approve", "Approve systems that enrol with the key automatically."));
        var uses = verb.Add(CliOptions.Number("--uses", "How many systems can enrol with the key; unlimited when left out.", minimum: 1));
        var tags = verb.Add(CliOptions.TagList("--tags", "Tags for the systems that enrol with the key."));
        var allowIp = verb.Add(CliOptions.Labelled("--allow-ip", "Allow enrolment only from this range.", "range"));
        var keepDisconnected = verb.Add(CliOptions.Duration("--keep-disconnected", "With --ephemeral: keep a system this long after it disconnects; 30m when left out."));
        var timing = KeyTiming.AddTo(verb);
        var notes = verb.Add(CliOptions.Text("--notes", "The key's notes."));

        // As in the portal, an ephemeral key always approves automatically and has unlimited uses
        // (portal-spa createEnrolmentKeySaga.ts:39-40, which sends Automatic and -1 for an ephemeral
        // key whatever the form holds), so a flag that sets either contradicts --ephemeral ("Command
        // options").
        Option[] generalPurposeOnly = [autoApprove, uses];
        verb.Check(context =>
        {
            if (context.Get(ephemeral) && generalPurposeOnly.FirstOrDefault(context.IsGiven) is { } option)
            {
                throw CliErrors.InvalidArgument(option.Name, $"An ephemeral key always approves automatically and has unlimited uses, so {option.Name} cannot be given with --ephemeral.");
            }
        });

        // The API takes a retention time on ephemeral keys only (portal
        // EnrolmentKeyCreateValidator.cs:24).
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
