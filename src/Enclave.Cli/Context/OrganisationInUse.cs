using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

// The organisation a command acts in.
internal sealed record OrganisationInUse(OrganisationGuid Id, IOrganisationScopedClient Client);
