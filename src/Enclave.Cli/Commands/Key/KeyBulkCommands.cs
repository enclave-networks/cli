using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// `key enable`, `key disable` and `key delete`: keys given by description, by --id, or as a key
/// list on stdin, sent in bulk calls of 200, printing { requested, affected }
/// (proposed-cli-surface.md "Several IDs").
/// </summary>
internal static class KeyBulkCommands
{
    public static CliVerb Enable()
    {
        var (verb, keys, ids) = Several("enable", "Enable keys; with --for or --until, enable one key until then.");
        var timing = KeyTiming.AddTo(verb);

        verb.SetHandler(async context =>
        {
            var expiry = timing.Expiry(context);
            var selection = await Items.ReadAsync(context, keys, ids, ListKind.Key, IdFormats.Int32);

            if (expiry is not { } until)
            {
                await RunAsync(context, selection, keys, (org, batch) => org.Client.EnrolmentKeys.BulkEnableAsync(batch));
                return;
            }

            // The API's timed enable has no bulk form, so --for and --until take one key and print
            // the key the API returns ("Several IDs").
            if (selection.Ids.Count + selection.Names.Count != 1)
            {
                var option = context.IsGiven(timing.For) ? (Option)timing.For : timing.Until;
                throw CliErrors.InvalidArgument(option.Name, $"{option.Name} enables one key, since the API's timed enable has no bulk form. Give one key.");
            }

            var org = await context.GetOrganisationAsync();
            var keyId = selection.Ids.Count == 1
                ? EnrolmentKeyId.FromInt(selection.Ids[0])
                : (await KeyLookup.FindAsync(context, org, selection.Names, keys.Name))[0];

            var key = await SingleItem.CallAsync(() => org.Client.EnrolmentKeys.EnableUntilAsync(keyId, until, timing.Action(context)));
            await context.Output.WriteAsync(key, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Disable()
    {
        var (verb, keys, ids) = Several("disable", "Disable keys, so no system can enrol with them.");

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, keys, ids, ListKind.Key, IdFormats.Int32);
            await RunAsync(context, selection, keys, (org, batch) => org.Client.EnrolmentKeys.BulkDisableAsync(batch));
        });

        return verb;
    }

    // One key makes the bulk call too, so the output is { requested, affected } however many keys
    // are given ("Several IDs"); Enclave.Sdk.Api 1.1.0 IEnrolmentKeysClient.DeleteAsync, the
    // single delete, is left unused.
    public static CliVerb Delete()
    {
        var (verb, keys, ids) = Several("delete", "Delete keys.");

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, keys, ids, ListKind.Key, IdFormats.Int32);
            await RunAsync(context, selection, keys, (org, batch) => org.Client.EnrolmentKeys.BulkDeleteAsync(batch));
        });

        return verb;
    }

    private static (CliVerb Verb, Argument<IReadOnlyList<string>> Keys, Option<IReadOnlyList<int>?> Ids) Several(string name, string description)
    {
        var verb = new CliVerb(name, $"{description} Takes key descriptions, IDs after --id, or \"-\" to read a list from stdin, and prints {{ requested, affected }}.", CommandScope.Organisation, changes: true);
        var keys = verb.Add(CliArguments.Many("key", "A key's description, matched whole and ignoring case."));
        var ids = verb.Add(CliOptions.IdList("--id", "Key IDs, separated by commas.", IdFormats.Int32));

        verb.Exclusive(keys, ids);

        return (verb, keys, ids);
    }

    // Descriptions are looked up with one list call; IDs from --id or stdin make none. Two
    // descriptions can name one key, so the IDs found are deduplicated as given IDs are ("Several
    // IDs").
    private static async Task RunAsync(
        CliContext context,
        ItemSelection<int> selection,
        Argument<IReadOnlyList<string>> keys,
        Func<OrganisationInUse, IReadOnlyList<EnrolmentKeyId>, Task<int>> call)
    {
        if (await WroteEmptyAsync(context, selection))
        {
            return;
        }

        var org = await context.GetOrganisationAsync();
        var keyIds = selection.Names.Count > 0
            ? (await KeyLookup.FindAsync(context, org, selection.Names, keys.Name)).Distinct().ToArray()
            : selection.Ids.Select(EnrolmentKeyId.FromInt).ToArray();

        var result = await Bulk.RunAsync(keyIds, batch => call(org, batch));
        await context.Output.WriteBulkAsync(result, context.CancellationToken);
    }

    // Items.ReadAsync refuses a command given no keys, so a selection with neither IDs nor names is
    // an empty list read from stdin. It prints { 0, 0 } and makes no call, the organisation lookup
    // included, so a pipeline fed by an empty list succeeds ("Several IDs"). A dry run goes on to
    // report what it would send, which for an empty list is no request ("Dry run").
    private static async Task<bool> WroteEmptyAsync(CliContext context, ItemSelection<int> selection)
    {
        if (selection.Ids.Count > 0 || selection.Names.Count > 0 || context.IsDryRun)
        {
            return false;
        }

        await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
        return true;
    }
}
