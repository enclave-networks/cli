using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Org;

internal static class OrgRemoveUserCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("remove-user", "Remove a user from the organisation, given by email address or by --id.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.OptionalText("email", "The user's email address."));
        var id = verb.Add(CliOptions.Id("--id", "The user's account ID.", IdFormats.Guid, "accountId"));

        verb.ExactlyOne(email, id);
        verb.RefuseBlank(email, context => context.Get(email), "an email address");

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
                // Not paged: one response holds every user (Enclave.Sdk.Api
                // OrganisationScopedClient.GetOrganisationUsersAsync).
                var users = await org.Client.GetOrganisationUsersAsync();
                accountId = NameLookup.Match([context.Get(email)!], users, user => user.EmailAddress, user => user.Id, email.Name, "user")[0].Id;
            }

            // A 404 means the account is not a member. Enclave.Sdk.Api checks the status before it
            // reads the body (OrganisationScopedClient.ReadModelAsync), so a failure without
            // problem details is reported too.
            var removed = await SingleItem.CallAsync(() => org.Client.RemoveUserAsync(accountId.ToString()));

            await context.Output.WriteAsync(removed, context.CancellationToken);
        });

        return verb;
    }
}
