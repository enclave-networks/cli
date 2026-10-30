namespace Enclave.Cli.Core;

/// <summary>
/// One of the two lists is empty. Names need a lookup; IDs have duplicates removed.
/// </summary>
/// <typeparam name="TId">The ID type.</typeparam>
internal sealed record ItemSelection<TId>(IReadOnlyList<TId> Ids, IReadOnlyList<string> Names)
{
    public int Count => Ids.Count + Names.Count;
}
