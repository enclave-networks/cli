using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Data;

namespace Enclave.Cli.Commands.Describe;

/// <summary>
/// `commands [&lt;command words&gt;...]`: every command, its arguments and options, the values each
/// option allows, and the error codes with their exit codes, as JSON (proposed-cli-surface.md
/// "Details"). It is generated from the command tree, so it describes every command the CLI has.
/// With --search-keys and the words of a list, the list's entry also carries the search keys the
/// API gives for its --filter.
/// </summary>
internal static class CommandsCommand
{
    private const string SystemList = "system list";

    private const string ListNames = "system list, key list, policy list or tag list";

    // The lists whose search keys Enclave.Sdk.Api 1.1.0 reads, by their command words. Each
    // GetSearchKeysAsync calls GET /org/{orgId}/<list>/meta/search-keys. system list --pending reads
    // the unapproved systems' keys, which have their own route (UnapprovedSystemsClient).
    private static readonly Dictionary<string, Func<IOrganisationScopedClient, Task<IReadOnlyList<SearchKey>>>> SearchKeyLists = new(StringComparer.Ordinal)
    {
        [SystemList] = client => client.EnrolledSystems.GetSearchKeysAsync(),
        ["key list"] = client => client.EnrolmentKeys.GetSearchKeysAsync(),
        ["policy list"] = client => client.Policies.GetSearchKeysAsync(),
        ["tag list"] = client => client.Tags.GetSearchKeysAsync(),
    };

    public static CliVerb Create()
    {
        // --search-keys reads from the organisation in use, so the verb takes --org and --org-id.
        var verb = new CliVerb(
            "commands",
            "Describe every command, its arguments and options, the values each option allows, and the error codes with their exit codes, as JSON. Given a command's words, describe that command, or every command under a noun. Needs no token and makes no call, except with --search-keys.",
            CommandScope.Organisation);

        var words = verb.Add(CliArguments.Many("words", "The words of a command or noun, such as: system list."));
        var searchKeys = verb.Add(CliOptions.Flag("--search-keys", $"With the words of {ListNames}, add the search keys that list's --filter accepts, as the API gives them. Makes one call, so needs a token and an organisation."));
        var pending = verb.Add(CliOptions.Flag("--pending", "With system list and --search-keys, give the search keys for the systems waiting for approval."));

        // Without --search-keys the command acts within no organisation, so these options would
        // change nothing; refusing them tells the caller so.
        verb.Requires(pending, searchKeys);
        verb.Requires(verb.OrgOption!, searchKeys);
        verb.Requires(verb.OrgIdOption!, searchKeys);

        verb.SetHandler(async context =>
        {
            Command start = context.ParseResult.RootCommandResult.Command;
            var path = new List<string>();

            // A plural noun is accepted here as on the command line, and the description uses the
            // singular names.
            foreach (var word in context.Get(words) ?? [])
            {
                start = start.Subcommands.FirstOrDefault(command => !command.Hidden && (command.Name == word || command.Aliases.Contains(word)))
                    ?? throw CliErrors.InvalidArgument(words.Name, $"`{string.Join(' ', [.. path, word])}` is not a command or noun. Run `{CliRootCommand.ProgramName} commands` for every command.");

                path.Add(start.Name);
            }

            // The arguments are checked in full before the organisation is asked for, which reads
            // the token: checks run in the order arguments, token, organisation, call
            // (proposed-cli-surface.md "Errors and exit codes").
            var readSearchKeys = context.Get(searchKeys)
                ? SearchKeysReader(string.Join(' ', path), context.Get(pending), searchKeys, pending)
                : null;

            var commands = new JsonArray();
            foreach (var (name, command) in Leaves(start, path))
            {
                commands.Add(Describe(name, command));
            }

            if (readSearchKeys is not null)
            {
                var org = await context.GetOrganisationAsync();
                var keys = await readSearchKeys(org.Client);

                // A list command has no subcommands, so it is the one entry.
                commands[0]!["searchKeys"] = JsonSerializer.SerializeToNode(keys, CliJson.Output);
            }

            var errors = new JsonArray(ErrorCode.All
                .Select(error => (JsonNode?)new JsonObject { ["code"] = error.Code, ["exitCode"] = error.ExitCode })
                .ToArray());

            await context.Output.WriteAsync(new JsonObject { ["commands"] = commands, ["errors"] = errors }, context.CancellationToken);
        });

        return verb;
    }

    // The call that reads the search keys for the command named, or exit 2 for a command that is
    // not one of the lists or --pending on a list other than system list.
    private static Func<IOrganisationScopedClient, Task<IReadOnlyList<SearchKey>>> SearchKeysReader(string command, bool pending, Option searchKeys, Option pendingOption)
    {
        if (!SearchKeyLists.TryGetValue(command, out var read))
        {
            var given = command.Length == 0 ? "No command was given." : $"`{command}` is not one of them.";
            throw CliErrors.InvalidArgument(CliVerb.KeyOf(searchKeys), $"{searchKeys.Name} takes the words of {ListNames}. {given}");
        }

        if (!pending)
        {
            return read;
        }

        return string.Equals(command, SystemList, StringComparison.Ordinal)
            ? client => client.UnapprovedSystems.GetSearchKeysAsync()
            : throw CliErrors.InvalidArgument(CliVerb.KeyOf(pendingOption), $"{pendingOption.Name} with {searchKeys.Name} takes {SystemList}, for the systems waiting for approval. `{command}` has no {pendingOption.Name}.");
    }

    // A command that runs has no subcommands; a noun's commands are its verbs and second-level
    // nouns' verbs.
    private static IEnumerable<(string Name, Command Command)> Leaves(Command command, List<string> path)
    {
        var subcommands = command.Subcommands.Where(subcommand => !subcommand.Hidden).ToArray();

        if (subcommands.Length == 0)
        {
            yield return (string.Join(' ', path), command);
            yield break;
        }

        foreach (var subcommand in subcommands)
        {
            foreach (var leaf in Leaves(subcommand, [.. path, subcommand.Name]))
            {
                yield return leaf;
            }
        }
    }

    private static JsonObject Describe(string name, Command command)
    {
        var arguments = new JsonArray(command.Arguments
            .Where(argument => !argument.Hidden)
            .Select(argument => (JsonNode?)new JsonObject { ["name"] = argument.Name })
            .ToArray());

        var options = new JsonArray();
        foreach (var option in HelpWriter.CommandOptions(command))
        {
            var entry = new JsonObject { ["name"] = option.Name };

            if (option is IChoiceOption choice)
            {
                entry["values"] = new JsonArray(choice.AllowedValues.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            }

            options.Add(entry);
        }

        return new JsonObject
        {
            ["command"] = name,
            ["description"] = command.Description,
            ["arguments"] = arguments,
            ["options"] = options,
        };
    }
}
