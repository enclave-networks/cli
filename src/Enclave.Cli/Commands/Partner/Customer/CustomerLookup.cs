using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Partner.Models;

namespace Enclave.Cli.Commands.Partner.Customer;

internal static class CustomerLookup
{
    public static Task<List<CustomerModel>> ReadAllAsync(ICustomersClient customers, CancellationToken cancellationToken) =>
        Paging.ReadAllAsync((page, perPage) => customers.GetCustomersAsync(pageNumber: page, perPage: perPage), cancellationToken);

    // Matches the name whole, ignoring case. No match, or several, exits 2 with the matches as
    // candidates.
    public static async Task<OrganisationGuid> IdAsync(ICustomersClient customers, string name, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customers);

        var all = await ReadAllAsync(customers, cancellationToken);

        return NameLookup.One(
            name,
            all,
            customer => customer.Name,
            customer => customer.Id,
            key,
            $"The partner has no customer named \"{name}\".",
            $"\"{name}\" names more than one of the partner's customers. Give the one meant with --org-id.").Id;
    }

    // Matches an owner or admin by email address, ignoring case. No match, or several, exits 2
    // with the matches as candidates; a customer the API does not know exits 5.
    public static async Task<AccountGuid> AdminIdAsync(ICustomersClient customers, OrganisationGuid customerId, string email, string key)
    {
        ArgumentNullException.ThrowIfNull(customers);

        // Not paged: one response holds every owner and admin (Enclave.Sdk.Api
        // CustomersClient.GetAdminsAsync).
        var admins = await SingleItem.CallAsync(() => customers.GetAdminsAsync(customerId));

        return NameLookup.One(
            email,
            admins,
            admin => admin.EmailAddress,
            admin => admin.Id,
            key,
            $"The customer has no admin with the email address \"{email}\".",
            $"More than one of the customer's admins has the email address \"{email}\". Give the one meant with --user-id.").Id;
    }

    // Matches a pending invite by email address, ignoring case. No match, or several, exits 2 with
    // the matches as candidates; a customer the API does not know exits 5.
    // The partner API cancels an invite by its ID, and the CLI takes no invite ID from the user
    // (spec/cli-surface.md "Partner API"), so the address names the invite.
    public static async Task<OrganisationInviteId> InviteIdAsync(ICustomersClient customers, OrganisationGuid customerId, string email, string key)
    {
        ArgumentNullException.ThrowIfNull(customers);

        // Not paged: one response holds every invite (Enclave.Sdk.Api
        // CustomersClient.GetPendingInvitesAsync).
        var invites = await SingleItem.CallAsync(() => customers.GetPendingInvitesAsync(customerId));

        return NameLookup.One(
            email,
            invites,
            invite => invite.EmailAddress,
            invite => invite.Id,
            key,
            $"The customer has no pending invite to \"{email}\".",
            $"The customer has more than one pending invite to \"{email}\".").Id;
    }
}
