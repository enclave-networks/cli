using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

/// <summary>
/// An organisation chosen by one source, before any lookup: by ID, or by a name still to be matched.
/// </summary>
/// <param name="Id">The ID, when the source gives one.</param>
/// <param name="Name">The name to match, when the source gives a name; for a saved default, the
/// name saved with its ID.</param>
/// <param name="Source">The option, variable or file path that made the choice.</param>
internal sealed record OrganisationChoice(OrganisationGuid? Id, string? Name, string Source);
