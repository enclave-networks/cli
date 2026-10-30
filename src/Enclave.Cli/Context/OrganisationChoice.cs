using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

/// <summary>
/// An organisation as one source chose it, before any lookup.
/// </summary>
/// <param name="Id">Null when the source gives only a name.</param>
/// <param name="Name">The name to match; for a saved default, the name saved with its ID.</param>
/// <param name="Source">The option, variable or file path that made the choice.</param>
internal sealed record OrganisationChoice(OrganisationGuid? Id, string? Name, string Source);
