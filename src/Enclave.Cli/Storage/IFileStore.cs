namespace Enclave.Cli.Storage;

/// <summary>
/// The CLI reaches files only through this, so tests can keep them in memory. Every path is fully
/// qualified; DiskFileStore throws ArgumentException for any other.
/// </summary>
internal interface IFileStore
{
    /// <summary>
    /// Null when the file does not exist.
    /// </summary>
    string? ReadText(string path);

    /// <summary>
    /// Creates or replaces the file in one step. A missing directory is created readable by the
    /// current user only (mode 0700 on Linux and macOS). With <paramref name="privateToUser"/>, only
    /// the current user can read the file (mode 0600 on Linux and macOS).
    /// </summary>
    void WriteText(string path, string text, bool privateToUser);

    /// <summary>
    /// False when there was no file.
    /// </summary>
    bool Delete(string path);
}
