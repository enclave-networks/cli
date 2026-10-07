using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Org;

/// <summary>
/// `org remove-user`: removes a user from the organisation, given by email address, looked up with
/// one call, or by account ID after --id, which makes no lookup (proposed-cli-surface.md "Names and
/// IDs").
/// </summary>
internal static class OrgRemoveUserCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("remove-user", "Remove a user from the organisation, given by email address or by --id.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.OptionalText("email", "The user's email address."));
        var id = verb.Add(CliOptions.Id("--id", "The user's account ID.", IdFormats.Guid, "accountId"));

        verb.ExactlyOne(email, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            AccountGuid accountId;

            if (context.Get(id) is { } guid)
            {
                accountId = AccountGuid.FromGuid(guid);
            }
            else
            {
                // GetOrganisationUsersAsync returns every user in one response
                // (OrganisationScopedClient.cs, Enclave.Sdk.Api 1.1.0), so the address is matched in
                // that one list.
                var users = await org.Client.GetOrganisationUsersAsync();
                accountId = NameLookup.Match([context.Get(email)!], users, user => user.EmailAddress, user => user.Id, email.Name, "user")[0].Id;
            }

            // One account named by ID or email address: a 404 means it is not a member, exit 5
            // ("Several IDs"). Enclave.Sdk.Api 1.1.0 checks the status of this response, so a
            // failure without problem details is reported too ("`Enclave.Sdk.Api` changes",
            // item 6).
            await SingleItem.CallAsync(() => org.Client.RemoveUserAsync(accountId.ToString()));

            // The API answers a removal with no body, which prints {} ("Several IDs").
            await context.Output.WriteNoBodyAsync(context.CancellationToken);
        });

        return verb;
    }
}
