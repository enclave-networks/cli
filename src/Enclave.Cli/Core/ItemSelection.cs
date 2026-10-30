namespace Enclave.Cli.Core;

// One of the two lists is empty. Names need a lookup; IDs have duplicates removed.
internal sealed record ItemSelection<TId>(IReadOnlyList<TId> Ids, IReadOnlyList<string> Names)
{
    public int Count => Ids.Count + Names.Count;
}
