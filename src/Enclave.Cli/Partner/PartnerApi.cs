using System.Text.Json;

namespace Enclave.Cli.Partner;

/// <summary>
/// The Enclave Partner API operations behind the <c>partner</c> commands. Enclave.Sdk.Api 1.0.4 has no partner clients
/// and no partner API base URL, so every operation throws <see cref="NotImplementedException"/>, which the commands
/// report as <c>not_implemented</c>. See "Partner API" and "Needs Enclave.Sdk.Api changes" in proposed-cli-surface.md.
/// </summary>
internal sealed class PartnerApi
{
    private const string NotImplementedMessage =
        "The partner API needs Enclave.Sdk.Api partner clients, which Enclave.Sdk.Api 1.0.4 does not have.";

    public Task<JsonElement> ListPartnersAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> GetPartnerAsync(Guid partnerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> UpdatePartnerAsync(Guid partnerId, JsonElement patch, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ListPartnerUsersAsync(Guid partnerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> UpdatePartnerUserAsync(
        Guid partnerId,
        Guid accountId,
        JsonElement patch,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> RemovePartnerUserAsync(Guid partnerId, Guid accountId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ListPartnerInvitesAsync(Guid partnerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> SendPartnerInviteAsync(Guid partnerId, string email, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> UpdatePartnerInviteAsync(
        Guid partnerId,
        string inviteId,
        JsonElement patch,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> CancelPartnerInviteAsync(Guid partnerId, string inviteId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ListCustomersAsync(Guid partnerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> GetCustomerAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> CreateCustomerAsync(Guid partnerId, JsonElement customer, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> UpdateCustomerAsync(
        Guid partnerId,
        Guid customerId,
        JsonElement patch,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ConvertCustomerAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ListCustomerAdminsAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> AddCustomerAdminAsync(
        Guid partnerId,
        Guid customerId,
        Guid accountId,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> RemoveCustomerAdminAsync(
        Guid partnerId,
        Guid customerId,
        Guid accountId,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> ListCustomerInvitesAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> SendCustomerInviteAsync(
        Guid partnerId,
        Guid customerId,
        string email,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> CancelCustomerInviteAsync(
        Guid partnerId,
        Guid customerId,
        string inviteId,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> EnableCustomerAutoSyncAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);

    public Task<JsonElement> DisableCustomerAutoSyncAsync(Guid partnerId, Guid customerId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedMessage);
}
