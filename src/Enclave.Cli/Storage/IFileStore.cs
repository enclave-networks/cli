namespace Enclave.Cli.Storage;

/// <summary>
/// Reads and writes the CLI's files. The CLI reaches files only through this, so tests can keep
/// them in memory.
/// </summary>
internal interface IFileStore
{
    /// <summary>
    /// The file's text, or null when the file does not exist.
    /// </summary>
    string? ReadText(string path);

    /// <summary>
    /// Creates or replaces the file with this text in one step, creating its directory when it is
    /// missing. With <paramref name="privateToUser"/>, only the current user can read what it
    /// creates: on Linux and macOS a new directory gets mode 0700 and the file mode 0600.
    /// </summary>
    void WriteText(string path, string text, bool privateToUser);

    /// <summary>
    /// Deletes the file, and returns false when there was no file.
    /// </summary>
    bool Delete(string path);
}
