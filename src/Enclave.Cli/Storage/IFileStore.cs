namespace Enclave.Cli.Storage;

// The CLI reaches files only through this, so tests can keep them in memory. Every path is fully
// qualified; DiskFileStore throws ArgumentException for any other.
internal interface IFileStore
{
    // Null when the file does not exist.
    string? ReadText(string path);

    // Creates or replaces the file in one step. A missing directory is created readable by the
    // current user only (mode 0700 on Linux and macOS). With privateToUser, only the current user
    // can read the file (mode 0600 on Linux and macOS).
    void WriteText(string path, string text, bool privateToUser);

    // False when there was no file.
    bool Delete(string path);
}
