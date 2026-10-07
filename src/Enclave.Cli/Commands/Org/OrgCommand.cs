using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Org;

/// <summary>
/// The `org` noun: the organisations the token sees, the default organisation, and the
/// organisation's own settings, users and invites (proposed-cli-surface.md "Commands").
/// </summary>
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
        var name = verb.Add(CliOptions.Text("--name", "The organisation's new name.", "name"));
        var website = verb.Add(CliOptions.Text("--website", "The organisation's website.", "url"));
        var phone = verb.Add(CliOptions.Text("--phone", "The organisation's phone number.", "phone"));

        verb.AtLeastOne(name, website, phone);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // An update sends only the fields given: a Name the caller did not give would rename the
            // organisation (proposed-cli-surface.md "Create and update").
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

            // GetOrganisationUsersAsync returns every user in one response
            // (OrganisationScopedClient.cs, Enclave.Sdk.Api 1.1.0), so there are no pages to read.
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

            // GetPendingInvitesAsync returns every invite in one response
            // (OrganisationScopedClient.cs, Enclave.Sdk.Api 1.1.0), so there are no pages to read.
            var invites = await org.Client.GetPendingInvitesAsync();

            await context.Output.WriteListAsync(ListKind.Invite, invites, context.CancellationToken);
        });

        return verb;
    }

    // A failed invite or cancel-invite is reported, with or without problem details: Enclave.Sdk.Api
    // 1.1.0 checks the status of these responses (proposed-cli-surface.md "`Enclave.Sdk.Api`
    // changes", item 6), and CliAction maps its exception like any other.
    private static CliVerb Invite()
    {
        var verb = new CliVerb("invite", "Invite someone to the organisation by email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The email address to invite."));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            await org.Client.InviteUserAsync(context.Get(email)!);

            // The API answers an invite with no body, which prints {} ("Several IDs").
            await context.Output.WriteNoBodyAsync(context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb CancelInvite()
    {
        var verb = new CliVerb("cancel-invite", "Cancel the pending invite to an email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The invited email address."));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // The main API cancels an invite by email address (CancelInviteAync), so there is nothing
            // to look up ("Partner API"). It answers 404 for an address with no pending invite (portal
            // OrganisationController.DeleteInvite), which a single-ID command reports as exit 5
            // ("Several IDs").
            await SingleItem.CallAsync(() => org.Client.CancelInviteAync(context.Get(email)!));

            await context.Output.WriteNoBodyAsync(context.CancellationToken);
        });

        return verb;
    }
}
