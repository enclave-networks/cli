namespace Enclave.Cli.Storage;

/// <summary>
/// The file store the CLI uses outside tests: files on disk.
/// </summary>
internal sealed class DiskFileStore : IFileStore
{
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    // The text goes to a new file beside the target, which is flushed to the disk and then renamed
    // over the target. A crash, a power loss or a full disk part-way through leaves the old file or
    // the new one, never a partial file, and the credentials file holds the token. The flush comes
    // before the rename because some filesystems can otherwise commit the rename before the data,
    // leaving an empty file after a power loss. File.Move with overwrite renames in place on the
    // same volume (rename(2) on Linux and macOS, MoveFileEx with MOVEFILE_REPLACE_EXISTING on
    // Windows).
    //
    // On Linux and macOS the new file is created with mode 0600, so a replaced file ends up private
    // even when the old one was readable by others. .NET's default would be 0666 less the umask,
    // usually 0644 (FileStreamOptions.UnixCreateMode). Windows has no mode bits: a file under the
    // user profile inherits the profile's access list, which other users cannot read.
    public void WriteText(string path, string text, bool privateToUser)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;

        // A directory the CLI creates gets mode 0700 whichever file it is created for: ~/.enclave
        // holds credentials.json, and `org use` before `login` creates it for cli.json, which holds
        // no secret, with the token written into it later. An existing directory keeps its mode:
        // ~/.enclave is shared with other Enclave tools, so the CLI sets the mode only on a directory
        // it creates.
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
        }

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (privateToUser && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        try
        {
            using (var stream = new FileStream(temporaryPath, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            // Present only when the write or the rename failed.
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public bool Delete(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
