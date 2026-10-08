namespace Enclave.Cli.Core;

/// <summary>
/// The items a command that takes several was given: IDs (after --id, from a list on stdin, or as
/// the arguments of a command whose arguments are IDs), or names still to be looked up. One of the
/// two is empty.
/// </summary>
/// <typeparam name="TId">The ID type.</typeparam>
/// <param name="Ids">The IDs given, duplicates removed, in the order given.</param>
/// <param name="Names">The names given, in the order given.</param>
internal sealed record ItemSelection<TId>(IReadOnlyList<TId> Ids, IReadOnlyList<string> Names);
