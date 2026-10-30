using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Network.Abstractions.NetworkPolicy;

namespace Enclave.Cli.Commands.Trust;

// The API's patch replaces the whole settings object (portal TrustRequirementPatchModel.Settings),
// so a --set- flag reads the requirement first and replaces only the settings of its own kind.
internal static class TrustUpdateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("update", "Change a trust requirement and print it. Each --set- flag replaces only the conditions of its own kind.", CommandScope.Organisation, changes: true);
        var requirement = verb.Add(CliArguments.OptionalName("trust", "The trust requirement's description, matched whole and ignoring case.", "a trust requirement's description"));
        var id = verb.Add(CliOptions.Id("--id", "The trust requirement's ID.", IdFormats.Int32));
        // The API refuses a blank description and takes empty notes, which clear them (portal
        // TrustRequirementPatchBasicValidator).
        var description = verb.Add(CliOptions.NonBlank("--description", "The trust requirement's description.", "a trust requirement's description"));
        var notes = verb.Add(CliOptions.Text("--notes", "The trust requirement's notes."));
        var tenant = verb.Add(CliOptions.Text("--set-tenant", "Replace the Entra ID tenant (azure).", "id"));
        var authorityUri = verb.Add(CliOptions.Text("--set-authority-uri", "Replace the authority's https address (okta, jumpcloud, duo, oidc).", "url"));
        var clientId = verb.Add(CliOptions.Text("--set-client-id", "Replace the client ID (okta, jumpcloud, duo, oidc).", "id"));
        var audience = verb.Add(CliOptions.Text("--set-audience", "Replace the audience (okta, jumpcloud, duo, oidc).", "value"));
        var claims = verb.Add(CliOptions.Repeated("--set-claim", "Replace the required claims.", "claim=value"));
        var allowIp = verb.Add(CliOptions.Labelled("--set-allow-ip", "Replace the allowed ranges, keeping the labels of ranges that stay.", "range", clears: true));
        var blockIp = verb.Add(CliOptions.Labelled("--set-block-ip", "Replace the blocked ranges, keeping the labels of ranges that stay.", "range", clears: true));
        var allowCountry = verb.Add(CliOptions.Labelled("--set-allow-country", "Replace the allowed countries, keeping the labels of countries that stay.", "code", clears: true));
        var blockCountry = verb.Add(CliOptions.Labelled("--set-block-country", "Replace the blocked countries, keeping the labels of countries that stay.", "code", clears: true));

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

        // Flags of both kinds cannot apply, whichever type the requirement has, so this needs no read.
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

            // As on trust create, a setting of only white space exits 2. "" goes on to the handler,
            // where it removes a setting the authority does not require.
            foreach (var (option, _) in settings)
            {
                if (context.Get(option) is { Length: > 0 } value && string.IsNullOrWhiteSpace(value))
                {
                    throw CliErrors.InvalidArgument(option.Name, $"{option.Name} takes a value, and the value given is blank.");
                }
            }

            if (!ListValues.Clears(context.Get(claims)))
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

            var updated = await SingleItem.CallAsync(() => patch.ApplyAsync());
            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }

    // The settings the authority takes (TrustSettings.KeysOf) are known only once the requirement
    // is read. What is not given goes back as it was.
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

            // "" removes the setting, since the API refuses an empty audience (portal
            // TrustRequirementSettingsUserAuthValidator).
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
            ? (ListValues.Clears(claimValues) ? [] : claimValues.Select(TrustSettings.Claim).ToArray())
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
                conditions = TrustSettings.Replace(conditions, kind, ListValues.Clears(values) ? [] : values);
            }
        }

        return new TrustRequirementSettingsModel(current.Settings.Configuration, conditions);
    }
}
