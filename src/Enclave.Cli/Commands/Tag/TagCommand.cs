using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Tags.Enums;

namespace Enclave.Cli.Commands.Tag;

/// <summary>
/// The `tag` noun (proposed-cli-surface.md "Commands"). Tags are given by name only.
/// </summary>
internal static class TagCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("tag", "tags", "Tags, which decide policy membership.");

        noun.Add(List());
        noun.Add(Show());
        noun.Add(TagSetCommand.Create());
        noun.Add(Delete());

        return noun;
    }

    private static CliVerb List()
    {
        var verb = new CliVerb("list", "List the tags, reading every page.", CommandScope.Organisation);
        var filter = verb.Add(CliOptions.Text("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));
        var sort = verb.Add(CliOptions.Enum("--sort", "The order to list tags in.", TagQuerySortOrder.Alphabetical, TagQuerySortOrder.RecentlyUsed, TagQuerySortOrder.ReferencedSystems));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var search = new SearchText(context.Get(filter)).ToSearchTerm();

            var tags = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.Tags.GetAsync(search, context.Get(sort), page, perPage),
                context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Tag, tags, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb Show()
    {
        var verb = new CliVerb("show", "Show a tag.", CommandScope.Organisation);
        var tag = verb.Add(CliArguments.Id("tag", "The tag's name.", IdFormats.Tag));

        verb.SetHandler(async context =>
        {
            var name = context.Get(tag)!;
            var org = await context.GetOrganisationAsync();

            // One tag named: a 404 means it does not exist, exit 5 ("Several IDs").
            var found = await SingleItem.CallAsync(() => org.Client.Tags.GetAsync(name));
            await context.Output.WriteAsync(found, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb Delete()
    {
        var verb = new CliVerb("delete", "Delete tags. Takes any number of tag names, or \"-\" to read a list from stdin, and prints { requested, affected }.", CommandScope.Organisation, changes: true);
        var tags = verb.Add(CliArguments.Many("tag", "A tag's name.", IdFormats.Tag));

        verb.SetHandler(async context =>
        {
            var names = await Items.ReadIdsAsync(context, tags, ListKind.Tag, IdFormats.Tag);

            // An empty list from stdin prints { 0, 0 } and makes no call, the organisation lookup
            // included, so a pipeline fed by an empty list succeeds ("Several IDs"). A dry run goes
            // on to report what it would send, which for an empty list is no request ("Dry run").
            if (names.Count == 0 && !context.IsDryRun)
            {
                await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
                return;
            }

            var org = await context.GetOrganisationAsync();

            // The bulk delete takes tag names or refs (TagsClient.DeleteTagsAsync, Enclave.Sdk.Api
            // 1.1.0). Every name has passed the tag rule, which no ref ("ref:" and hex digits) meets,
            // so each is read as a name.
            var result = await Bulk.RunAsync(names, batch => org.Client.Tags.DeleteTagsAsync([.. batch]));
            await context.Output.WriteBulkAsync(result, context.CancellationToken);
        });

        return verb;
    }
}
