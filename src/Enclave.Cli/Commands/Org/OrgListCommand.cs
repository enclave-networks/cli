using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Org;

/// <summary>
/// `org list`: the organisations the token sees. It needs no organisation chosen; it is how a
/// caller finds one.
/// </summary>
internal static class OrgListCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the organisations the token sees, with the token's role in each.");

        verb.SetHandler(async context =>
        {
            // GetOrganisationsAsync returns the whole list in one response (EnclaveClient.cs,
            // Enclave.Sdk.Api 1.1.0), so there are no pages to read.
            var organisations = await context.GetClient().GetOrganisationsAsync();

            await context.Output.WriteListAsync(ListKind.Org, organisations, context.CancellationToken);
        });

        return verb;
    }
}
