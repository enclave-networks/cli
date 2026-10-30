using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

// A file store held in memory. It keeps whether each file was written as private, so a test can
// check the CLI asked for that.
internal sealed class MemoryFileStore : IFileStore
{
    // The CLI and the tests both build paths from CliRun.Home, so the same file always has the same
    // spelling.
    private readonly Dictionary<string, (string Text, bool Private)> _files = new(StringComparer.Ordinal);

    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    // Every path that holds a file, in no particular order.
    public IReadOnlyCollection<string> Paths => _files.Keys;

    public string? ReadText(string path)
    {
        _read.Add(Normalise(path));

        return _files.TryGetValue(Normalise(path), out var file) ? file.Text : null;
    }

    public void WriteText(string path, string text, bool privateToUser) =>
        _files[Normalise(path)] = (text, privateToUser);

    public bool Delete(string path) => _files.Remove(Normalise(path));

    public bool Exists(string path) => _files.ContainsKey(Normalise(path));

    // Whether anything read this path through ReadText, with or without a file there.
    public bool WasRead(string path) => _read.Contains(Normalise(path));

    // Whether the file's last write asked for it to be private to the user. Fails the test when
    // there is no such file.
    public bool IsPrivate(string path) =>
        _files.TryGetValue(Normalise(path), out var file)
            ? file.Private
            : throw new AssertionException($"Expected a file at {path}; the store holds: {string.Join(", ", _files.Keys)}");

    // GetFullPath does not touch the disk.
    private static string Normalise(string path) => Path.GetFullPath(path);
}
