namespace Enclave.Cli.Storage;

internal sealed class DiskFileStore : IFileStore
{
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public string? ReadText(string path)
    {
        RequireFullyQualified(path);

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

    // Written beside the target, flushed to the disk, then renamed over it (File.Move with
    // overwrite: rename(2), or MoveFileEx with MOVEFILE_REPLACE_EXISTING), so a crash, power loss or
    // full disk leaves the old file or the new one, never a partial file. Without the flush some
    // filesystems commit the rename before the data, leaving an empty file after a power loss.
    //
    // A private file is created with mode 0600, so a replaced file ends up private even when the old
    // one was not; .NET's default is 0666 less the umask (FileStreamOptions.UnixCreateMode). Windows
    // has no mode bits: a file under the user profile inherits the profile's access list.
    public void WriteText(string path, string text, bool privateToUser)
    {
        RequireFullyQualified(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;

        // A new directory gets 0700 whichever file it is for: `org use` before `login` creates
        // ~/.enclave for cli.json, and credentials.json goes there later. An existing directory keeps
        // its mode, since ~/.enclave is shared with other Enclave tools.
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
        RequireFullyQualified(path);

        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // A path that is not fully qualified resolves against the current directory: the CLI would read
    // whatever credentials.json is there and send the token to its baseUrl, or write the token
    // there. EnclaveDirectory.HomeFrom refuses such a home directory; this refuses such a path
    // whatever the caller.
    private static void RequireFullyQualified(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"The CLI reads and writes files only at a fully qualified path, and {path} is not one.", nameof(path));
        }
    }
}
