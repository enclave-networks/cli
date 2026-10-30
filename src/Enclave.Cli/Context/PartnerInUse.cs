using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

// The partner a partner command acts for. Source is --partner-id, ENCLAVE_PARTNER_ID, or the path
// of cli.json. Client's Customers client covers the customer routes.
internal sealed record PartnerInUse(Guid Id, string Source, IPartnerClient Client);
