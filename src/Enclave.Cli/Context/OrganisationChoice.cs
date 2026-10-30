using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

// An organisation as one source chose it, before any lookup. Id is null when the source gives only
// a name. Name is the name to match; for a saved default, the name saved with its ID. Source is
// the option, variable or file path that made the choice.
internal sealed record OrganisationChoice(OrganisationGuid? Id, string? Name, string Source);
