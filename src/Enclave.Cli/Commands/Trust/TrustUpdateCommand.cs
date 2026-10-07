using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Network.Abstractions.NetworkPolicy;

namespace Enclave.Cli.Commands.Trust;

/// <summary>
/// `trust update`: patches the description, notes and settings given (proposed-cli-surface.md
/// "Command options"). The API's patch replaces the whole settings object (portal
/// TrustRequirementPatchModel.Settings), so a --set- flag reads the requirement first and replaces
/// only the setting or conditions of its own kind.
/// </summary>
internal static class TrustUpdateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("update", "Change a trust requirement and print it. Each --set- flag replaces only the conditions of its own kind.", CommandScope.Organisation, changes: true);
        var requirement = verb.Add(CliArguments.OptionalText("trust", "The trust requirement's description, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The trust requirement's ID.", IdFormats.Int32));
        var description = verb.Add(CliOptions.Text("--description", "The trust requirement's description."));
        var notes = verb.Add(CliOptions.Text("--notes", "The trust requirement's notes."));
        var tenant = verb.Add(CliOptions.Text("--set-tenant", "Replace the Entra ID tenant (azure).", "id"));
        var authorityUri = verb.Add(CliOptions.Text("--set-authority-uri", "Replace the authority's https address (okta, jumpcloud, duo, oidc).", "url"));
        var clientId = verb.Add(CliOptions.Text("--set-client-id", "Replace the client ID (okta, jumpcloud, duo, oidc).", "id"));
        var audience = verb.Add(CliOptions.Text("--set-audience", "Replace the audience (okta, jumpcloud, duo, oidc).", "value"));
        var claims = verb.Add(CliOptions.Repeated("--set-claim", "Replace the required claims.", "claim=value"));
        var allowIp = verb.Add(CliOptions.Labelled("--set-allow-ip", "Replace the allowed ranges, keeping the labels of ranges that stay.", "range"));
        var blockIp = verb.Add(CliOptions.Labelled("--set-block-ip", "Replace the blocked ranges, keeping the labels of ranges that stay.", "range"));
        var allowCountry = verb.Add(CliOptions.Labelled("--set-allow-country", "Replace the allowed countries, keeping the labels of countries that stay.", "code"));
        var blockCountry = verb.Add(CliOptions.Labelled("--set-block-country", "Replace the blocked countries, keeping the labels of countries that stay.", "code"));

        (Option<string?> Option, string Key)[] settings =
        [
            (tenant, TrustSettings.TenantKey),
            (authorityUri, TrustSettings.AuthorityUriKey),
            (clientId, TrustSettings.ClientIdKey),
            (audience, TrustSettings.AudienceKey),
        ];
        Option[] signIn = [tenant, authorityUri, clientId, audience, claims];
        (Option<IReadOnlyList<LabelledValue>?> Option, TrustSettings.PublicIpKind Kind)[] publicIp =
        [
            (allowIp, TrustSettings.AllowedIp),
            (blockIp, TrustSettings.BlockedIp),
            (allowCountry, TrustSettings.AllowedCountry),
            (blockCountry, TrustSettings.BlockedCountry),
        ];
        Option[] publicIpOptions = [.. publicIp.Select(entry => entry.Option)];

        verb.ExactlyOne(requirement, id);
        verb.AtLeastOne([description, notes, .. signIn, .. publicIpOptions]);

        // A trust requirement is a sign-in or a public IP one, so flags of both kinds cannot apply
        // to it, whichever type it has.
        verb.Check(context =>
        {
            var signInGiven = signIn.FirstOrDefault(context.IsGiven);
            var publicIpGiven = publicIpOptions.FirstOrDefault(context.IsGiven);

            if (signInGiven is not null && publicIpGiven is not null)
            {
                throw CliErrors.InvalidArgument(publicIpGiven.Name, $"{signInGiven.Name} changes a sign-in requirement and {publicIpGiven.Name} a public IP requirement; a trust requirement is one or the other.");
            }
        });
        verb.Check(context =>
        {
            if (context.Get(authorityUri) is { } uri && !TrustSettings.IsHttpsUri(uri))
            {
                throw CliErrors.InvalidArgument(authorityUri.Name, "--set-authority-uri takes an https address.");
            }

            if (context.Get(clientId) is { Length: 0 })
            {
                throw CliErrors.InvalidArgument(clientId.Name, "--set-client-id takes the client ID, which the authority requires.");
            }

            if (!Clears(context.Get(claims)))
            {
                TrustSettings.CheckClaims(claims.Name, context.Get(claims));
            }
        });

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var requirementId = TrustRequirementId.FromInt(await TrustLookup.IdAsync(context, org, context.Get(id), context.Get(requirement), requirement.Name));

            TrustRequirementSettingsModel? newSettings = null;

            if (signIn.FirstOrDefault(context.IsGiven) is { } signInOption)
            {
                var current = await SingleItem.CallAsync(() => org.Client.TrustRequirements.GetAsync(requirementId));
                newSettings = SignIn(context, current, signInOption, settings, claims);
            }
            else if (publicIpOptions.FirstOrDefault(context.IsGiven) is { } publicIpOption)
            {
                var current = await SingleItem.CallAsync(() => org.Client.TrustRequirements.GetAsync(requirementId));
                newSettings = PublicIp(context, current, publicIpOption, publicIp);
            }

            var patch = org.Client.TrustRequirements.Update(requirementId);

            if (context.Get(description) is { } newDescription)
            {
                patch.Set(model => model.Description, newDescription);
            }

            if (context.Get(notes) is { } newNotes)
            {
                patch.Set(model => model.Notes, newNotes);
            }

            if (newSettings is not null)
            {
                patch.Set(model => model.Settings, newSettings);
            }

            // One trust requirement named: a 404 means it does not exist, exit 5 ("Several IDs").
            var updated = await SingleItem.CallAsync(() => patch.ApplyAsync());
            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }

    // update takes the create flags of the requirement's own type, and each authority takes only
    // its own settings (portal TrustRequirementSettingsUserAuthValidator.cs:34-48), which are known
    // once the requirement is read. The configuration's other keys and, without --set-claim, the
    // claims go back as they were.
    private static TrustRequirementSettingsModel SignIn(
        CliContext context,
        TrustRequirementModel current,
        Option given,
        (Option<string?> Option, string Key)[] settings,
        Option<IReadOnlyList<string>?> claims)
    {
        if (current.Type != TrustRequirementType.UserAuthentication)
        {
            throw CliErrors.InvalidArgument(given.Name, $"{given.Name} changes a sign-in requirement, and trust requirement {current.Id} is a public IP requirement.");
        }

        var configuration = new Dictionary<string, string?>(current.Settings.Configuration, StringComparer.Ordinal);
        var authority = configuration.GetValueOrDefault(TrustSettings.AuthorityKey);
        var takes = TrustSettings.KeysOf(authority);

        foreach (var (option, key) in settings)
        {
            if (context.Get(option) is not { } value)
            {
                continue;
            }

            if (!takes.Contains(key))
            {
                throw CliErrors.InvalidArgument(option.Name, $"Trust requirement {current.Id} signs in with {authority ?? "no authority"}, which does not take {option.Name}.");
            }

            // An empty value removes a setting the authority does not require, since the API
            // rejects an empty audience (portal TrustRequirementSettingsUserAuthValidator.cs:49).
            if (value.Length == 0)
            {
                configuration.Remove(key);
            }
            else
            {
                configuration[key] = value;
            }
        }

        var conditions = context.Get(claims) is { } claimValues
            ? (Clears(claimValues) ? [] : claimValues.Select(TrustSettings.Claim).ToArray())
            : current.Settings.Conditions ?? [];

        return new TrustRequirementSettingsModel(configuration, conditions);
    }

    private static TrustRequirementSettingsModel PublicIp(
        CliContext context,
        TrustRequirementModel current,
        Option given,
        (Option<IReadOnlyList<LabelledValue>?> Option, TrustSettings.PublicIpKind Kind)[] publicIp)
    {
        if (current.Type != TrustRequirementType.PublicIp)
        {
            throw CliErrors.InvalidArgument(given.Name, $"{given.Name} changes a public IP requirement, and trust requirement {current.Id} is a sign-in requirement.");
        }

        var conditions = current.Settings.Conditions ?? [];

        foreach (var (option, kind) in publicIp)
        {
            if (context.Get(option) is { } values)
            {
                conditions = TrustSettings.Replace(conditions, kind, Clears(values) ? [] : values);
            }
        }

        return new TrustRequirementSettingsModel(current.Settings.Configuration, conditions);
    }

    // A --set- list flag given "" alone clears its list ("Details").
    private static bool Clears(IReadOnlyList<LabelledValue> values) => values is [{ Value.Length: 0, HasLabel: false }];

    private static bool Clears(IReadOnlyList<string>? values) => values is [{ Length: 0 }];
}
