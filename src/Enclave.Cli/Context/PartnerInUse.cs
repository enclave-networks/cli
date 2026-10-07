using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

/// <summary>
/// The partner a partner command acts for, with the Enclave.Sdk.Api client for it.
/// </summary>
/// <param name="Id">The partner's ID.</param>
/// <param name="Source">What chose it: --partner-id, ENCLAVE_PARTNER_ID, or the path of cli.json.</param>
/// <param name="Client">The partner API client every partner call goes through; its Customers
/// client covers the customer routes.</param>
internal sealed record PartnerInUse(Guid Id, string Source, IPartnerClient Client);
