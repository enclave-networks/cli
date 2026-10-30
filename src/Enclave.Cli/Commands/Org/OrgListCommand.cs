using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Org;

internal static class OrgListCommand
{
    public static CliVerb Create()
    {
        // No organisation scope: org list is how a caller finds an organisation.
        var verb = new CliVerb("list", "List the organisations the token sees, with the token's role in each.");

        verb.SetHandler(async context =>
        {
            // Not paged: one response holds every organisation (Enclave.Sdk.Api
            // EnclaveClient.GetOrganisationsAsync).
            var organisations = await context.GetClient().GetOrganisationsAsync();

            await context.Output.WriteListAsync(ListKind.Org, organisations, context.CancellationToken);
        });

        return verb;
    }
}
