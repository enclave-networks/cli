using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

// The partner a partner command acts for. Client's Customers client covers the customer routes.
internal sealed record PartnerInUse(Guid Id, IPartnerClient Client);
