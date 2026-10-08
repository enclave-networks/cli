using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Context;

/// <summary>
/// The organisation a command acts in, with the Enclave.Sdk.Api client for it.
/// </summary>
/// <param name="Id">The organisation's ID.</param>
/// <param name="Name">Its name, or null when it was given by ID, since then no lookup is made (a
/// saved default keeps the name it was saved with).</param>
/// <param name="Role">The token's role in it, or null when no lookup was made.</param>
/// <param name="Source">What chose it: --org, --org-id, ENCLAVE_ORG, ENCLAVE_ORG_ID, the path of
/// cli.json, or only-organisation.</param>
/// <param name="Client">The client every call within the organisation goes through.</param>
internal sealed record OrganisationInUse(
    OrganisationGuid Id,
    string? Name,
    UserOrganisationRole? Role,
    string Source,
    IOrganisationScopedClient Client);
