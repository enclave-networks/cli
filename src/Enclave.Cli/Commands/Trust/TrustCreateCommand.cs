using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Cli.Core;
using Enclave.Sdk.Network.Abstractions.NetworkPolicy;

namespace Enclave.Cli.Commands.Trust;

/// <summary>
/// `trust create &lt;description&gt;`: a sign-in requirement with --authority, or a public IP
/// requirement with --allow-* and --block-* (proposed-cli-surface.md "Command options").
/// </summary>
internal static class TrustCreateCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("create", "Create a trust requirement and print it: --authority makes a sign-in requirement, --allow-* and --block-* a public IP requirement.", CommandScope.Organisation, changes: true);
        var description = verb.Add(CliArguments.Text("description", "The trust requirement's description."));
        var notes = verb.Add(CliOptions.Text("--notes", "The trust requirement's notes."));
        var authority = verb.Add(CliOptions.ChoiceText("--authority", "Require a sign-in with this authority.", TrustSettings.Authorities));
        var tenant = verb.Add(CliOptions.Text("--tenant", "With --authority azure: the Entra ID tenant.", "id"));
        var authorityUri = verb.Add(CliOptions.Text("--authority-uri", "With --authority okta, jumpcloud, duo or oidc: the authority's https address.", "url"));
        var clientId = verb.Add(CliOptions.Text("--client-id", "With --authority okta, jumpcloud, duo or oidc: the client ID.", "id"));
        var audience = verb.Add(CliOptions.Text("--audience", "With --authority okta, jumpcloud, duo or oidc: the audience.", "value"));
        var claims = verb.Add(CliOptions.Repeated("--claim", "Require this claim in the sign-in token.", "claim=value"));
        var allowIp = verb.Add(CliOptions.Labelled("--allow-ip", "Allow public addresses in this range.", "range"));
        var blockIp = verb.Add(CliOptions.Labelled("--block-ip", "Block public addresses in this range.", "range"));
        var allowCountry = verb.Add(CliOptions.Labelled("--allow-country", "Allow public addresses in this country, an ISO 3166 two-letter code.", "code"));
        var blockCountry = verb.Add(CliOptions.Labelled("--block-country", "Block public addresses in this country, an ISO 3166 two-letter code.", "code"));

        (Option<string?> Option, string Key)[] settings =
        [
            (tenant, TrustSettings.TenantKey),
            (authorityUri, TrustSettings.AuthorityUriKey),
            (clientId, TrustSettings.ClientIdKey),
            (audience, TrustSettings.AudienceKey),
        ];
        Option[] signIn = [authority, tenant, authorityUri, clientId, audience, claims];
        (Option<IReadOnlyList<LabelledValue>?> Option, TrustSettings.PublicIpKind Kind)[] publicIp =
        [
            (allowIp, TrustSettings.AllowedIp),
            (blockIp, TrustSettings.BlockedIp),
            (allowCountry, TrustSettings.AllowedCountry),
            (blockCountry, TrustSettings.BlockedCountry),
        ];

        // The type comes from the flags, and a trust requirement is one type or the other
        // ("Command options"); the API requires a type (portal TrustRequirementCreateModel.Type).
        verb.Check(context =>
        {
            var signInGiven = signIn.FirstOrDefault(context.IsGiven);
            var publicIpGiven = publicIp.Select(entry => (Option)entry.Option).FirstOrDefault(context.IsGiven);

            if (signInGiven is not null && publicIpGiven is not null)
            {
                throw CliErrors.InvalidArgument(publicIpGiven.Name, $"{signInGiven.Name} makes a sign-in requirement and {publicIpGiven.Name} a public IP requirement; a trust requirement is one or the other.");
            }

            if (signInGiven is null && publicIpGiven is null)
            {
                throw CliErrors.InvalidArgument(authority.Name, "Give --authority for a sign-in requirement, or --allow-ip, --block-ip, --allow-country or --block-country for a public IP requirement.");
            }
        });

        foreach (var option in signIn.Skip(1))
        {
            verb.Requires(option, authority);
        }

        // A setting given "" carries no value. The API refuses an empty authority address, client
        // ID or audience (portal TrustRequirementSettingsUserAuthValidator.cs:47-49) and stores an
        // empty tenant, since for azure it checks only which keys the configuration holds (same
        // file, 35-38). Each exits 2, as --set-client-id "" does on trust update.
        verb.Check(context =>
        {
            foreach (var (option, _) in settings)
            {
                if (context.Get(option) is { Length: 0 })
                {
                    throw CliErrors.InvalidArgument(option.Name, $"{option.Name} takes a value; \"\" sets nothing.");
                }
            }
        });

        verb.Check(context =>
        {
            if (context.Get(authority) is not { } name)
            {
                return;
            }

            var takes = TrustSettings.KeysOf(name);

            foreach (var (option, key) in settings)
            {
                if (context.IsGiven(option) && !takes.Contains(key))
                {
                    throw CliErrors.InvalidArgument(option.Name, $"--authority {name} does not take {option.Name}.");
                }

                if (!context.IsGiven(option) && TrustSettings.Requires(name, key))
                {
                    throw CliErrors.InvalidArgument(option.Name, $"--authority {name} requires {option.Name}.");
                }
            }

            if (context.Get(authorityUri) is { } uri && !TrustSettings.IsHttpsUri(uri))
            {
                throw CliErrors.InvalidArgument(authorityUri.Name, "--authority-uri takes an https address.");
            }
        });
        verb.Check(context => TrustSettings.CheckClaims(claims.Name, context.Get(claims)));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var model = context.Get(authority) is { } name
                ? new TrustRequirementCreateModel(context.Get(description)!, TrustRequirementType.UserAuthentication, context.Get(notes), SignIn(context, name, settings, claims))
                : new TrustRequirementCreateModel(context.Get(description)!, TrustRequirementType.PublicIp, context.Get(notes), PublicIp(context, publicIp));

            var requirement = await org.Client.TrustRequirements.CreateAsync(model);
            await context.Output.WriteAsync(requirement, context.CancellationToken);
        });

        return verb;
    }

    private static TrustRequirementSettingsModel SignIn(CliContext context, string authority, (Option<string?> Option, string Key)[] settings, Option<IReadOnlyList<string>?> claims)
    {
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal) { [TrustSettings.AuthorityKey] = authority };

        foreach (var (option, key) in settings)
        {
            if (context.Get(option) is { } value)
            {
                configuration[key] = value;
            }
        }

        return new TrustRequirementSettingsModel(configuration, (context.Get(claims) ?? []).Select(TrustSettings.Claim).ToArray());
    }

    private static TrustRequirementSettingsModel PublicIp(CliContext context, (Option<IReadOnlyList<LabelledValue>?> Option, TrustSettings.PublicIpKind Kind)[] publicIp)
    {
        var conditions = publicIp
            .SelectMany(entry => LabelledEntry.FromGiven(context.Get(entry.Option)).Select(value => TrustSettings.Condition(entry.Kind, value)))
            .ToArray();

        return new TrustRequirementSettingsModel(new Dictionary<string, string?>(StringComparer.Ordinal), conditions);
    }
}
