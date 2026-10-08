using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// A file store held in memory, which CliRun gives the CLI so tests never write to disk. It keeps
/// whether each file was written as private, so a test can check the CLI asked for that.
/// </summary>
internal sealed class MemoryFileStore : IFileStore
{
    // Paths are compared exactly. The CLI and the tests both build paths from CliRun.Home, so the
    // same file always has the same spelling.
    private readonly Dictionary<string, (string Text, bool Private)> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Every path that holds a file, in no particular order.
    /// </summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;

    public string? ReadText(string path) =>
        _files.TryGetValue(Normalise(path), out var file) ? file.Text : null;

    public void WriteText(string path, string text, bool privateToUser) =>
        _files[Normalise(path)] = (text, privateToUser);

    public bool Delete(string path) => _files.Remove(Normalise(path));

    public bool Exists(string path) => _files.ContainsKey(Normalise(path));

    /// <summary>
    /// Whether the file's last write asked for it to be private to the user. Fails the test when
    /// there is no such file.
    /// </summary>
    public bool IsPrivate(string path) =>
        _files.TryGetValue(Normalise(path), out var file)
            ? file.Private
            : throw new AssertionException($"Expected a file at {path}; the store holds: {string.Join(", ", _files.Keys)}");

    // GetFullPath only rewrites the string (separators, "." and ".." segments); it does not touch
    // the disk.
    private static string Normalise(string path) => Path.GetFullPath(path);
}
