using System.CommandLine;
using System.Text.Json.Nodes;
using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

/// <summary>
/// Chooses the organisation a command acts in (proposed-cli-surface.md "Context").
/// </summary>
internal static class OrganisationResolver
{
    public const string NameVariable = "ENCLAVE_ORG";

    public const string IdVariable = "ENCLAVE_ORG_ID";

    /// <summary>
    /// The source status reports when the token sees one organisation and nothing else chose it.
    /// </summary>
    public const string OnlyOrganisation = "only-organisation";

    /// <summary>
    /// Refuses an empty or blank organisation name given to <paramref name="option"/>, --org, with
    /// the verb's other argument checks.
    /// </summary>
    public static void RefuseBlankName(CliVerb verb, Option<string?> option)
    {
        ArgumentNullException.ThrowIfNull(option);

        RefuseBlankName(verb, option, context => context.Get(option));
    }

    /// <summary>
    /// Refuses an empty or blank organisation name given to <paramref name="argument"/>, such as
    /// org use's name, with the verb's other argument checks.
    /// </summary>
    public static void RefuseBlankName(CliVerb verb, Argument<string?> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        RefuseBlankName(verb, argument, context => context.Get(argument));
    }

    /// <summary>
    /// The organisation the command line, the environment or cli.json chooses, before any lookup;
    /// null when none does. Precedence is --org or --org-id, then ENCLAVE_ORG or ENCLAVE_ORG_ID,
    /// then the saved default, and a lower source is not read once a higher one gives a value, so a
    /// malformed lower source does not fail the command ("Details"). An empty variable counts as
    /// unset. A malformed source that is read, or both variables set, exits 2.
    /// </summary>
    public static OrganisationChoice? Choose(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var verb = context.Verb;

        if (verb?.OrgIdOption is { } idOption && context.Get(idOption) is { } id)
        {
            return new OrganisationChoice(OrganisationGuid.FromGuid(id), null, idOption.Name);
        }

        if (verb?.OrgOption is { } nameOption && context.IsGiven(nameOption))
        {
            return new OrganisationChoice(null, context.Get(nameOption) ?? string.Empty, nameOption.Name);
        }

        var host = context.Host;
        var nameVariable = host.GetEnvironmentVariable(NameVariable) is { Length: > 0 } name ? name : null;
        var idVariable = host.GetEnvironmentVariable(IdVariable) is { Length: > 0 } text ? text : null;

        if (nameVariable is not null && idVariable is not null)
        {
            throw CliErrors.InvalidArgument(IdVariable, $"{NameVariable} and {IdVariable} are both set, and contradict each other. Set one of them.");
        }

        if (idVariable is not null)
        {
            return IdFormats.Guid.TryParse(idVariable, out var guid)
                ? new OrganisationChoice(OrganisationGuid.FromGuid(guid), null, IdVariable)
                : throw CliErrors.InvalidArgument(IdVariable, $"{IdVariable} takes an organisation ID: {IdFormats.Guid.Describe}. {NameVariable} takes a name.");
        }

        if (nameVariable is not null)
        {
            return new OrganisationChoice(null, nameVariable, NameVariable);
        }

        return Saved(host);
    }

    /// <summary>
    /// The organisation for a command: by ID with no call, or by name or with none chosen after one
    /// GetOrganisationsAsync call. With none chosen, the token's only organisation is used, and
    /// several exit 2 with no_org and the organisations as candidates.
    /// </summary>
    public static async Task<OrganisationInUse> ResolveAsync(CliContext context, EnclaveClient client)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(client);

        var choice = Choose(context);

        if (choice?.Id is { } id)
        {
            context.Verbose($"Organisation {id} from {choice.Source}.");
            return new OrganisationInUse(id, choice.Name, null, choice.Source, client.CreateOrganisationClient(id));
        }

        context.Verbose("Looking up the token's organisations.");
        var organisations = await client.GetOrganisationsAsync();

        if (choice?.Name is { } name)
        {
            var match = MatchName(name, organisations, choice.Source);
            context.Verbose($"Organisation {match.OrgId} from {choice.Source}.");
            return new OrganisationInUse(match.OrgId, match.OrgName, match.Role, choice.Source, client.CreateOrganisationClient(match));
        }

        if (organisations.Count != 1)
        {
            var message = organisations.Count == 0
                ? "No organisation is chosen, and the token sees none."
                : $"No organisation is chosen, and the token sees {organisations.Count}. Choose one with --org or --org-id, {NameVariable} or {IdVariable}, or save a default with `enclave-cli org use`.";

            throw new CliException(ErrorCode.NoOrg, message) { Candidates = Candidates(organisations) };
        }

        var only = organisations[0];
        context.Verbose($"Organisation {only.OrgId}, the only one the token sees.");
        return new OrganisationInUse(only.OrgId, only.OrgName, only.Role, OnlyOrganisation, client.CreateOrganisationClient(only));
    }

    /// <summary>
    /// The one organisation whose name is <paramref name="name"/>, matched whole and ignoring case.
    /// No match, or several, exits 2 with the matches as candidates (none for no match).
    /// </summary>
    public static AccountOrganisationModel MatchName(string name, IReadOnlyList<AccountOrganisationModel> organisations, string key)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(organisations);

        var matches = organisations.Where(organisation => string.Equals(organisation.OrgName, name, StringComparison.OrdinalIgnoreCase)).ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw CliErrors.NameNotUnique(key, $"The token sees no organisation named \"{name}\".", []),
            _ => throw CliErrors.NameNotUnique(key, $"{matches.Length} organisations are named \"{name}\". Choose one by ID with --org-id.", Candidates(matches)),
        };
    }

    /// <summary>
    /// Organisations as error candidates, { "id", "name" }.
    /// </summary>
    public static JsonArray Candidates(IEnumerable<AccountOrganisationModel> organisations)
    {
        ArgumentNullException.ThrowIfNull(organisations);

        return new JsonArray(organisations
            .Select(organisation => (JsonNode?)new JsonObject
            {
                ["id"] = organisation.OrgId.ToString(),
                ["name"] = organisation.OrgName,
            })
            .ToArray());
    }

    // An empty or blank name names no organisation. Passed on, it would cost the lookup a name makes
    // and then match nothing, and that lookup needs the token, so the same command line would exit 3
    // without a token and 2 with one. Refused with the other argument checks, it exits 2 before the
    // token and any call whatever the token (proposed-cli-surface.md "Errors and exit codes").
    private static void RefuseBlankName(CliVerb verb, Symbol symbol, Func<CliContext, string?> value)
    {
        ArgumentNullException.ThrowIfNull(verb);

        verb.Check(context =>
        {
            if (value(context) is { } name && string.IsNullOrWhiteSpace(name))
            {
                throw CliErrors.InvalidArgument(CliVerb.KeyOf(symbol), $"{CliVerb.Describe(symbol)} takes an organisation name, and the value given is blank.");
            }
        });
    }

    // A default in a shape org use does not save, such as an ID given as a bare string, names no
    // organisation, so it exits 2 naming the file, as a malformed ID does. Only a missing default
    // falls through to the lookup.
    private static OrganisationChoice? Saved(CliHost host)
    {
        if (SettingsFile.PathFor(host) is not { } path || SettingsFile.Read(host)?["org"] is not { } saved)
        {
            return null;
        }

        if (saved is not JsonObject savedOrg
            || savedOrg["id"] is not JsonValue value
            || !value.TryGetValue(out string? text)
            || !IdFormats.Guid.TryParse(text, out var guid))
        {
            throw CliErrors.InvalidArgument($"The default organisation in {path} has no valid ID. Save it again with `enclave-cli org use`.");
        }

        var name = savedOrg["name"] is JsonValue savedName && savedName.TryGetValue(out string? nameText) ? nameText : null;

        return new OrganisationChoice(OrganisationGuid.FromGuid(guid), name, path);
    }
}
