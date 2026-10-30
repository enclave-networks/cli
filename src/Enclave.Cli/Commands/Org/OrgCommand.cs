using System.CommandLine;
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

    /// <summary>
    /// Refuses an email address that is empty or only white space with the other argument checks,
    /// before the token and any call.
    /// </summary>
    // An empty or blank address names no one (proposed-cli-surface.md "Details"). Passed on, invite
    // and cancel-invite send it, and the API refuses it (portal
    // CreateOrganisationInviteModelValidator.cs:16 and DeleteOrganisationInviteModelValidator.cs:16,
    // EmailAddress, which fails a string with no "@" after its first character: FluentValidation
    // 11.9.0, AspNetCoreCompatibleEmailValidator.IsValid), and remove-user's lookup matches no
    // member; both come after the token, so the command would fail one way with a token and another
    // without ("Errors and exit codes").
    internal static void RefuseBlankEmail(CliVerb verb, Symbol symbol, Func<CliContext, string?> value)
    {
        ArgumentNullException.ThrowIfNull(verb);

        verb.Check(context =>
        {
            if (value(context) is { } email && string.IsNullOrWhiteSpace(email))
            {
                throw CliErrors.InvalidArgument(CliVerb.KeyOf(symbol), $"{CliVerb.Describe(symbol)} takes an email address, and the value given is blank.");
            }
        });
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
        // The API refuses an empty name and stores one of white space, since it checks only that
        // the name is at least one character long (portal
        // Enclave.Api/Modules/OrganisationManagement/Organisation/Validators/OrganisationPatchModelValidator.cs:11,
        // Length(1, ...): FluentValidation 11.9.0, LengthValidator, counts white space; the handler
        // passes the name on as given, Handlers/OrganisationModifyHandler.cs:41-60). A blank
        // name names nothing, and --org refuses one, so an organisation named so could not be
        // chosen by name; both exit 2 before the token ("Details"). The website and phone number
        // are not validated (same file), so "" clears them.
        var name = verb.Add(CliOptions.NonBlank("--name", "The organisation's new name.", "the organisation's new name", "name"));
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
            // (OrganisationScopedClient.cs, Enclave.Sdk.Api), so there are no pages to read.
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
            // (OrganisationScopedClient.cs, Enclave.Sdk.Api), so there are no pages to read.
            var invites = await org.Client.GetPendingInvitesAsync();

            await context.Output.WriteListAsync(ListKind.Invite, invites, context.CancellationToken);
        });

        return verb;
    }

    // A failed invite or cancel-invite is reported, with or without problem details: Enclave.Sdk.Api
    // checks the status of these responses before it reads the body
    // (OrganisationScopedClient.ReadModelAsync; proposed-cli-surface.md "`Enclave.Sdk.Api`
    // changes", item 6), and CliAction maps its exception like any other.
    private static CliVerb Invite()
    {
        var verb = new CliVerb("invite", "Invite someone to the organisation by email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The email address to invite."));

        RefuseBlankEmail(verb, email, context => context.Get(email));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // The API answers an invite with the invite's model (portal
            // OrganisationController.CreateInvite), which InviteUserAsync returns (Enclave.Sdk.Api).
            // It holds the address the invite was first sent to, which can differ in case
            // from the one given, so the output shows the invite as the API holds it.
            var invite = await org.Client.InviteUserAsync(context.Get(email)!);

            await context.Output.WriteAsync(invite, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb CancelInvite()
    {
        var verb = new CliVerb("cancel-invite", "Cancel the pending invite to an email address.", CommandScope.Organisation, changes: true);
        var email = verb.Add(CliArguments.Text("email", "The invited email address."));

        RefuseBlankEmail(verb, email, context => context.Get(email));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // The main API cancels an invite by email address (CancelInviteAync), so there is nothing
            // to look up ("Partner API"). It answers 404 for an address with no pending invite (portal
            // OrganisationController.DeleteInvite), which a single-ID command reports as exit 5
            // ("Several IDs").
            //
            // The API answers a cancelled invite with the invite's model (portal
            // OrganisationController.DeleteInvite), which CancelInviteAync returns (Enclave.Sdk.Api),
            // and a single-ID command prints the model ("Several IDs").
            var cancelled = await SingleItem.CallAsync(() => org.Client.CancelInviteAync(context.Get(email)!));

            await context.Output.WriteAsync(cancelled, context.CancellationToken);
        });

        return verb;
    }
}
