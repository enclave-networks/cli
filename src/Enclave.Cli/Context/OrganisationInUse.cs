using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

// The organisation a command acts in. Name is null when it was given by ID, the saved default
// included, since no lookup is made then. Role, the token's role in it, is null when no lookup was
// made. Source is --org, --org-id, ENCLAVE_CLI_ORG, ENCLAVE_CLI_ORG_ID, the path of credentials.json,
// or only-organisation.
internal sealed record OrganisationInUse(
    OrganisationGuid Id,
    string? Name,
    UserOrganisationRole? Role,
    string Source,
    IOrganisationScopedClient Client);
