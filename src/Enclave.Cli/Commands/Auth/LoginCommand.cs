using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Auth;

// spec/cli-surface.md "Login, logout and status".
internal static class LoginCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "login",
            "Check a personal access token with one call and save it in ~/.enclave/credentials.json. The token comes from stdin with --token-stdin, or from ENCLAVE_TOKEN. Prints the organisations the token sees, and saves the default organisation when it sees one.");

        var tokenStdin = verb.Add(CliOptions.Flag("--token-stdin", "Read the token from stdin, without its trailing newline. Takes precedence over ENCLAVE_TOKEN."));
        EnclaveDirectory.RequireHome(verb);

        verb.SetHandler(async context =>
        {
            var host = context.Host;
            var token = await ReadTokenAsync(context, context.Get(tokenStdin));

            // The file's baseUrl is kept and the token checked there, so a file pointing at another
            // API address keeps pointing there; partnerApiBaseUrl is kept too, since staging means
            // setting both (spec/cli-surface.md "Partner API").
            var stored = CredentialsFile.Read(host);
            var access = new ApiAccess(
                token,
                context.Get(tokenStdin) ? "stdin" : ApiAccess.TokenVariable,
                ApiAccess.BaseUrlFrom(host, stored),
                ApiAccess.PartnerApiBaseUrlFrom(host, stored));
            context.Verbose($"Checking the token at {access.BaseUrl}.");

            // A token the API refuses throws here, before anything is written, so it never replaces a
            // working one.
            var organisations = await context.CreateClient(access).GetOrganisationsAsync();

            CredentialsFile.Write(host, token, stored?.BaseUrl ?? access.BaseUrl.AbsoluteUri, stored?.PartnerApiBaseUrl);
            context.Verbose($"Saved the token in {CredentialsFile.PathFor(host)}.");

            // With several, the default the user chose with org use stays as it is.
            if (organisations.Count == 1)
            {
                var only = organisations[0];
                SettingsFile.SaveOrganisation(host, only.OrgId, only.OrgName);
                context.Verbose($"Saved {only.OrgId} as the default organisation.");
            }

            await context.Output.WriteListAsync(ListKind.Org, organisations, context.CancellationToken);
        });

        return verb;
    }

    // login never prompts, so stdin that is a terminal is refused without being read.
    private static async Task<string> ReadTokenAsync(CliContext context, bool fromStdin)
    {
        if (fromStdin)
        {
            if (context.Host.StdinIsTerminal)
            {
                throw CliErrors.InvalidArgument("--token-stdin", "--token-stdin reads the token from stdin, and stdin is a terminal. Pipe the token in: enclave-cli login --token-stdin < token-file.");
            }

            var text = await context.Host.Stdin.ReadToEndAsync(context.CancellationToken);

            return ApiAccess.TokenFrom(text, "stdin") ?? throw CliErrors.TokenMissing("stdin holds no token.");
        }

        return ApiAccess.TokenFrom(context.Host.GetEnvironmentVariable(ApiAccess.TokenVariable), ApiAccess.TokenVariable)
            ?? throw CliErrors.TokenMissing("No token: give --token-stdin with the token on stdin, or set ENCLAVE_TOKEN. Personal access tokens are created on the account page of the Enclave portal.");
    }
}
