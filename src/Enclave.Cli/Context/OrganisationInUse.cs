using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

// The organisation a command acts in. Name is null when it was given by ID, since no lookup is made
// then; a saved default keeps the name it was saved with. Role, the token's role in it, is null
// when no lookup was made. Source is --org, --org-id, ENCLAVE_ORG, ENCLAVE_ORG_ID, the path of
// cli.json, or only-organisation.
internal sealed record OrganisationInUse(
    OrganisationGuid Id,
    string? Name,
    UserOrganisationRole? Role,
    string Source,
    IOrganisationScopedClient Client);
