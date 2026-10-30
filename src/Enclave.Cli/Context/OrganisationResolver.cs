using System.CommandLine;
using System.Text.Json.Nodes;
using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

// spec/cli-surface.md "Context".
internal static class OrganisationResolver
{
    public const string NameVariable = "ENCLAVE_ORG";

    public const string IdVariable = "ENCLAVE_ORG_ID";

    // The source when the token sees one organisation and nothing else chose it.
    public const string OnlyOrganisation = "only-organisation";

    // Null when no source chooses one. A lower source is not read once a higher one gives a value,
    // so a malformed lower source does not fail the command ("Details"). An empty variable counts
    // as unset. A malformed source that is read, or both variables set, exits 2.
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

    // By ID with no call; by name, or with none chosen, after one GetOrganisationsAsync call. With
    // none chosen the token's only organisation is used; with several it exits 2 with no_org.
    public static async Task<OrganisationInUse> ResolveAsync(CliContext context, EnclaveClient client)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(client);

        var choice = Choose(context);

        if (choice?.Id is { } id)
        {
            context.Diagnostic($"Organisation {id} from {choice.Source}.");
            return new OrganisationInUse(id, choice.Name, null, choice.Source, client.CreateOrganisationClient(id));
        }

        context.Diagnostic("Looking up the token's organisations.");
        var organisations = await client.GetOrganisationsAsync();

        if (choice?.Name is { } name)
        {
            var match = MatchName(name, organisations, choice.Source);
            context.Diagnostic($"Organisation {match.OrgId} from {choice.Source}.");
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
        context.Diagnostic($"Organisation {only.OrgId}, the only one the token sees.");
        return new OrganisationInUse(only.OrgId, only.OrgName, only.Role, OnlyOrganisation, client.CreateOrganisationClient(only));
    }

    // Matched whole and ignoring case. No match, or several, exits 2 with the matches as
    // candidates.
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

    private static OrganisationChoice? Saved(CliHost host) =>
        SettingsFile.ReadDefault(host, "org", "organisation", "enclave-cli org use") is { } saved
            ? new OrganisationChoice(OrganisationGuid.FromGuid(saved.Id), saved.Name, saved.Path)
            : null;
}
