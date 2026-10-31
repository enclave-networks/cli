using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Spectre.Console;

namespace Enclave.Cli.Commands.Auth;

// spec/cli-surface.md "Login, logout and status", "Environments".
internal static class LoginCommand
{
    // The trace's token source for a token typed at the question.
    private const string Prompt = "prompt";

    private const int NoDefault = -1;

    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "login",
            "Saves a personal access token and a default organisation.");

        // Staging is for Enclave's own staff, so help and `commands` leave the option out.
        var staging = verb.Add(CliOptions.Flag("--staging", "Log in to staging."));
        staging.Hidden = true;
        EnclaveDirectory.RequireHome(verb);

        verb.SetHandler(async context =>
        {
            var host = context.Host;
            ApiAccess.RefuseEnvironmentWithoutToken(host);

            if (!host.StdinIsTerminal || !host.StderrIsTerminal)
            {
                throw CliErrors.TokenMissing($"login asks for a personal access token, and stdin or stderr is not a terminal. Run `enclave-cli login` in a terminal such as Windows Terminal, cmd or PowerShell, or set {ApiAccess.TokenVariable}.");
            }

            var (apiUrl, partnerApiUrl) = context.Get(staging)
                ? (host.StagingApiUrl, host.StagingPartnerApiUrl)
                : (host.DefaultApiUrl, host.DefaultPartnerApiUrl);
            var path = CredentialsFile.PathFor(host) ?? throw EnclaveDirectory.NoHome();
            var stored = CredentialsFile.Read(host);

            // A saved token is offered only for the address it was saved for, since a token goes only
            // to the environment it came with ("Options on every command"). A file that names no
            // apiBaseUrl was saved for production's.
            var saved = (stored?.ApiBaseUrl is { } savedApi
                    ? Uri.TryCreate(savedApi, UriKind.Absolute, out var savedUrl) && savedUrl == apiUrl
                    : apiUrl == host.DefaultApiUrl)
                && ApiAccess.Clean(stored?.Token) is not null;

            var console = host.CreatePromptConsole();
            var question = $"Personal access token for {apiUrl.Authority}:" + (saved ? " [saved]" : string.Empty);
            var answer = await new TextPrompt<string>(Markup.Escape(question)).Secret('*').AllowEmpty().ShowAsync(console, context.CancellationToken);

            var access = ApiAccess.TokenFrom(answer, Prompt) is { } typed
                ? new ApiAccess(typed, Prompt, apiUrl, partnerApiUrl)
                : saved
                    ? new ApiAccess(ApiAccess.TokenFrom(stored!.Token, path)!, path, apiUrl, partnerApiUrl)
                    : throw CliErrors.TokenMissing("No token was given. Paste a personal access token when asked; personal access tokens are created on the account page of the Enclave portal.");

            // A token the API refuses throws here, before anything is written, so it never replaces a
            // working one.
            var organisations = await context.CreateClient(access).GetOrganisationsAsync();
            var chosen = organisations.Count == 0
                ? null
                : await ChooseAsync(console, organisations, OrganisationResolver.Saved(host)?.Id, context.CancellationToken);

            // Enclave.Sdk.Api's partner API address is production's, so it is saved only for staging
            // (EnclaveClientOptions.PartnerApiBaseUrl).
            context.Trace.Saved(CredentialsFile.Write(
                host,
                access.Token,
                apiUrl.AbsoluteUri,
                context.Get(staging) ? host.StagingPartnerApiUrl.AbsoluteUri : null,
                chosen?.OrgId));

            await context.Output.WriteListAsync(ListKind.Org, organisations, context.CancellationToken);
        });

        return verb;
    }

    // The organisation picked, or null for No default. The saved default comes first when the token
    // sees it, then the rest in the API's order. Names are markup to Spectre.Console, so each is
    // escaped.
    private static async Task<AccountOrganisationModel?> ChooseAsync(IAnsiConsole console, IReadOnlyList<AccountOrganisationModel> organisations, OrganisationGuid? savedId, CancellationToken cancellationToken)
    {
        var ordered = organisations.OrderBy(organisation => organisation.OrgId.Equals(savedId) ? 0 : 1).ToArray();

        var picked = await new SelectionPrompt<int>()
            .Title("Default organisation")
            .EnableSearch()
            .AddChoices(Enumerable.Range(0, ordered.Length).Append(NoDefault))
            .UseConverter(index => index == NoDefault ? "No default" : Markup.Escape(ordered[index].OrgName))
            .ShowAsync(console, cancellationToken);

        return picked == NoDefault ? null : ordered[picked];
    }
}
