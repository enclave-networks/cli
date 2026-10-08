using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Partner.Models;

namespace Enclave.Cli.Commands.Partner.Customer;

/// <summary>
/// Reads the partner's customers, and finds a customer by name and a customer's admin or pending
/// invite by email address (proposed-cli-surface.md "Names and IDs").
/// </summary>
internal static class CustomerLookup
{
    /// <summary>
    /// Every customer of the partner, reading every page.
    /// </summary>
    public static Task<List<CustomerModel>> ReadAllAsync(ICustomersClient customers, CancellationToken cancellationToken) =>
        Paging.ReadAllAsync((page, perPage) => customers.GetCustomersAsync(pageNumber: page, perPage: perPage), cancellationToken);

    /// <summary>
    /// The organisation ID of the one customer whose name matches <paramref name="name"/> whole,
    /// ignoring case, read with the customer list. No match, or several, exits 2 with the matches
    /// as candidates.
    /// </summary>
    public static async Task<OrganisationGuid> IdAsync(ICustomersClient customers, string name, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customers);

        var all = await ReadAllAsync(customers, cancellationToken);

        return One(
            name,
            all,
            customer => customer.Name,
            customer => customer.Id,
            key,
            $"The partner has no customer named \"{name}\".",
            $"\"{name}\" names more than one of the partner's customers. Give the one meant with --org-id.").Id;
    }

    /// <summary>
    /// The account ID of the one owner or admin of the customer whose email address matches
    /// <paramref name="email"/>, ignoring case, read with the customer's admins. No match, or
    /// several, exits 2 with the matches as candidates; a customer the API does not know exits 5.
    /// </summary>
    public static async Task<AccountGuid> AdminIdAsync(ICustomersClient customers, OrganisationGuid customerId, string email, string key)
    {
        ArgumentNullException.ThrowIfNull(customers);

        // GetAdminsAsync returns every owner and admin in one response (CustomersClient.GetAdminsAsync,
        // Enclave.Sdk.Api 1.1.0), so the address is matched in that one list.
        var admins = await SingleItem.CallAsync(() => customers.GetAdminsAsync(customerId));

        return One(
            email,
            admins,
            admin => admin.EmailAddress,
            admin => admin.Id,
            key,
            $"The customer has no admin with the email address \"{email}\".",
            $"More than one of the customer's admins has the email address \"{email}\". Give the one meant with --user-id.").Id;
    }

    /// <summary>
    /// The ID of the customer's one pending invite whose email address matches
    /// <paramref name="email"/>, ignoring case, read with the customer's invites. No match, or
    /// several, exits 2 with the matches as candidates; a customer the API does not know exits 5.
    /// </summary>
    // The partner API cancels an invite by its ID, and the CLI never takes an invite ID from the user
    // (proposed-cli-surface.md "Partner API", "ID checks"), so the address is the only way to name
    // one.
    public static async Task<OrganisationInviteId> InviteIdAsync(ICustomersClient customers, OrganisationGuid customerId, string email, string key)
    {
        ArgumentNullException.ThrowIfNull(customers);

        // GetPendingInvitesAsync returns every invite in one response
        // (CustomersClient.GetPendingInvitesAsync, Enclave.Sdk.Api 1.1.0).
        var invites = await SingleItem.CallAsync(() => customers.GetPendingInvitesAsync(customerId));

        return One(
            email,
            invites,
            invite => invite.EmailAddress,
            invite => invite.Id,
            key,
            $"The customer has no pending invite to \"{email}\".",
            $"The customer has more than one pending invite to \"{email}\".").Id;
    }

    // NameLookup.Match matches the same way, and its message for several matches names --id, which
    // no partner customer command takes: a customer's ID goes after --org-id and an admin's after
    // --user-id, and an invite has no ID option. The match is repeated here so each message names
    // the option the command takes. The error has NameLookup's shape: invalid_argument under the
    // key the name came from, with the matches as { "id", "name" } candidates.
    private static TItem One<TItem, TId>(
        string name,
        IReadOnlyCollection<TItem> items,
        Func<TItem, string?> nameOf,
        Func<TItem, TId> idOf,
        string key,
        string noMatch,
        string several)
    {
        var matches = items.Where(item => string.Equals(nameOf(item), name, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        var candidates = new JsonArray(matches
            .Select(item => (JsonNode?)new JsonObject
            {
                ["id"] = JsonSerializer.SerializeToNode(idOf(item), CliJson.Compact),
                ["name"] = nameOf(item),
            })
            .ToArray());

        throw CliErrors.NameNotUnique(key, matches.Length == 0 ? noMatch : several, candidates);
    }
}
