using System.CommandLine;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Org;

internal static class OrgCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("org", "orgs", "The organisations the token sees, the default organisation, and the organisation's settings, users and invites.");

        noun.Add(OrgListCommand.Create());
        noun.Add(OrgUseCommand.Create());
        noun.Add(Show());
        noun.Add(Update());
        noun.Add(ListUsers());
        noun.Add(OrgRemoveUserCommand.Create());
        noun.Add(ListInvites());
        noun.Add(Invite());
        noun.Add(CancelInvite());

        return noun;
    }

    private static CliVerb Show()
    {
        var verb = new CliVerb("show", "Show the organisation's properties.", CommandScope.Organisation);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var properties = await org.Client.GetAsync();

            await context.Output.WriteAsync(properties, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb Update()
    {
        var verb = new CliVerb("update", "Change the organisation's name, website or phone number, and print its properties.", CommandScope.Organisation, changes: true);
        // The API stores a name of white space (portal OrganisationPatchModelValidator checks only
        // its length), which --org could not then choose, so a blank name exits 2 ("Details"). The
        // website and phone number are not validated, so "" clears them.
        var name = verb.Add(CliOptions.NonBlank("--name", "The organisation's new name.", "the organisation's new name", "name"));
        var website = verb.Add(CliOptions.Text("--website", "The organisation's website.", "url"));
        var phone = verb.Add(CliOptions.Text("--phone", "The organisation's phone number.", "phone"));

        verb.AtLeastOne(name, website, phone);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            var patch = org.Client.Update();

            if (context.Get(name) is { } newName)
            {
                patch.Set(model => model.Name, newName);
            }

            if (context.Get(website) is { } url)
            {
                patch.Set(model => model.Website, url);
            }

            if (context.Get(phone) is { } number)
            {
                patch.Set(model => model.Phone, number);
            }

            var properties = await patch.ApplyAsync();

            await context.Output.WriteAsync(properties, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb ListUsers()
    {
        var verb = new CliVerb("list-users", "List the organisation's users.", CommandScope.Organisation);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // Not paged: one response holds every user (Enclave.Sdk.Api
            // OrganisationScopedClient.GetOrganisationUsersAsync).
            var users = await org.Client.GetOrganisationUsersAsync();

            await context.Output.WriteListAsync(ListKind.User, users, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb ListInvites()
    {
        var verb = new CliVerb("list-invites", "List the organisation's pending invites.", CommandScope.Organisation);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // Not paged: one response holds every invite (Enclave.Sdk.Api
            // OrganisationScopedClient.GetPendingInvitesAsync).
            var invites = await org.Client.GetPendingInvitesAsync();

            await context.Output.WriteListAsync(ListKind.Invite, invites, context.CancellationToken);
        });

        return verb;
    }

    // Enclave.Sdk.Api checks the status before it reads the body
    // (OrganisationScopedClient.ReadModelAsync), so a failed invite or cancel-invite without
    // problem details is reported like any other.
    private static CliVerb Invite()
    {
        var verb = new CliVerb("invite", "Invite someone to the organisation by email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The email address to invite."));

        verb.RefuseBlank(email, context => context.Get(email), "an email address");

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // The API's invite holds the address it was first sent to, which can differ in case from
            // the one given (portal OrganisationController.CreateInvite).
            var invite = await org.Client.InviteUserAsync(context.Get(email)!);

            await context.Output.WriteAsync(invite, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb CancelInvite()
    {
        var verb = new CliVerb("cancel-invite", "Cancel the pending invite to an email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The invited email address."));

        verb.RefuseBlank(email, context => context.Get(email), "an email address");

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // The API cancels an invite by email address, so there is no lookup, and answers 404
            // for an address with no pending invite (portal OrganisationController.DeleteInvite).
            var cancelled = await SingleItem.CallAsync(() => org.Client.CancelInviteAync(context.Get(email)!));

            await context.Output.WriteAsync(cancelled, context.CancellationToken);
        });

        return verb;
    }
}
