using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Storage;

// These are the only tests in the suite that touch the disk; every other test gives the CLI a
// MemoryFileStore. Each test works in a fresh directory under the system temporary directory, which
// stands in for the user's home directory, and TearDown deletes it, so the real home directory and
// ~/.enclave are never read or written.
public class DiskFileStoreTests
{
    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode ReadableByOthers = UserOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryReadableByOthers =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly DiskFileStore _store = new();

    private string _home;

    private string EnclaveDirectory => Path.Combine(_home, ".enclave");

    private string CredentialsPath => Path.Combine(EnclaveDirectory, "credentials.json");

    [SetUp]
    public void CreateTheHomeDirectory()
    {
        _home = Path.Combine(Path.GetTempPath(), "enclave-cli-disk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    [TearDown]
    public void DeleteTheHomeDirectory()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    // A command reads credentials.json and cli.json when they exist and works without them, so a
    // missing file is an ordinary outcome for ReadText and is reported as null.
    [Test]
    public void ReadText_returns_null_when_the_file_does_not_exist()
    {
        Directory.CreateDirectory(EnclaveDirectory);

        Assert.That(_store.ReadText(CredentialsPath), Is.Null);
    }

    // On a machine where nothing has been saved, ~/.enclave itself is missing. .NET reports a missing
    // directory with DirectoryNotFoundException and a missing file with FileNotFoundException
    // (File.ReadAllText, https://learn.microsoft.com/dotnet/api/system.io.file.readalltext, .NET 10),
    // and both mean there is no file to read.
    [Test]
    public void ReadText_returns_null_when_the_directory_does_not_exist()
    {
        Assert.That(_store.ReadText(CredentialsPath), Is.Null);
    }

    // login writes credentials.json on a machine where ~/.enclave does not exist yet, so a write
    // creates the directory it needs. Both values of privateToUser are covered because a private
    // write also creates the directory, with mode 0700 on Linux and macOS.
    [Test]
    public void WriteText_creates_the_missing_directory_and_the_file([Values] bool privateToUser)
    {
        _store.WriteText(CredentialsPath, "{}", privateToUser);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(EnclaveDirectory), Is.True, "The directory was not created.");
            Assert.That(File.Exists(CredentialsPath), Is.True, "The file was not created.");
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo("{}"));
        });
    }

    // ~/.enclave holds credentials.json, so the directory is readable by the user only, 0700 on Linux
    // and macOS (proposed-cli-surface.md "Login, logout and status"), whichever file's write creates
    // it: `org use` before `login` writes cli.json, which holds no secret and is written with
    // privateToUser false, and a later login writes the token into the same directory. Windows has
    // no mode bits; a directory under the user profile inherits the profile's access list.
    [Test]
    public void WriteText_creates_a_missing_directory_readable_by_the_user_only([Values] bool privateToUser)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        _store.WriteText(Path.Combine(EnclaveDirectory, "cli.json"), "{}", privateToUser);

        Assert.That(File.GetUnixFileMode(EnclaveDirectory), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    // ~/.enclave is shared with other Enclave tools, so the CLI sets the mode only of a directory it
    // creates (proposed-cli-surface.md "Login, logout and status": login and org use create it as
    // 0700). The directory here is 0755, a mode another tool or a person could have given it, and it
    // must stay 0755.
    [Test]
    public void WriteText_leaves_the_mode_of_an_existing_directory([Values] bool privateToUser)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        Directory.CreateDirectory(EnclaveDirectory);
        File.SetUnixFileMode(EnclaveDirectory, DirectoryReadableByOthers);

        _store.WriteText(CredentialsPath, "{}", privateToUser);

        Assert.That(File.GetUnixFileMode(EnclaveDirectory), Is.EqualTo(DirectoryReadableByOthers));
    }

    // credentials.json holds the token, so login writes it private to the user, and the file is
    // readable by the user only: 0600 on Linux and macOS ("Login, logout and status"). .NET would
    // otherwise create it 0666 less the umask, usually 0644 (FileStreamOptions.UnixCreateMode,
    // .NET 10).
    [Test]
    public void WriteText_private_to_the_user_gives_the_file_mode_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        _store.WriteText(CredentialsPath, "{}", privateToUser: true);

        Assert.That(File.GetUnixFileMode(CredentialsPath), Is.EqualTo(UserOnly));
    }

    // A credentials file another tool wrote, or a person created, can be readable by others. Once
    // the CLI writes a token into it, it is readable by the user only: a file whose mode a rewrite
    // kept would leave the new token readable by others. The old file is 0644.
    [Test]
    public void WriteText_private_to_the_user_over_a_file_readable_by_others_gives_mode_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, """{ "personalAccessToken": "old" }""");
        File.SetUnixFileMode(CredentialsPath, ReadableByOthers);

        _store.WriteText(CredentialsPath, "{}", privateToUser: true);

        var mode = File.GetUnixFileMode(CredentialsPath);
        Assert.Multiple(() =>
        {
            Assert.That(mode, Is.EqualTo(UserOnly));
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo("{}"));
        });
    }

    // login and org use rewrite credentials.json and cli.json, so a write replaces the whole text:
    // nothing of a longer old file is left after the new text, and no other file is left beside it.
    [Test]
    public void WriteText_replaces_the_text_of_an_existing_file([Values] bool privateToUser)
    {
        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, """{ "personalAccessToken": "a much longer old token" }""");

        _store.WriteText(CredentialsPath, "{}", privateToUser);

        Assert.Multiple(() =>
        {
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo("{}"));
            Assert.That(Directory.GetFileSystemEntries(EnclaveDirectory), Is.EqualTo(new[] { CredentialsPath }));
        });
    }

    // logout deletes credentials.json and reports in its "deleted" field whether there was a file to
    // delete, so Delete reports that too. Afterwards the token is gone from the disk.
    [Test]
    public void Delete_removes_the_file_and_returns_true()
    {
        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, "{}");

        var deleted = _store.Delete(CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(File.Exists(CredentialsPath), Is.False, "The file is still on the disk.");
        });
    }

    // logout succeeds on a machine with no saved token and reports that there was nothing to delete,
    // so a missing file is an ordinary outcome for Delete and is reported as false.
    [Test]
    public void Delete_returns_false_when_the_file_does_not_exist()
    {
        Directory.CreateDirectory(EnclaveDirectory);

        Assert.That(_store.Delete(CredentialsPath), Is.False);
    }

    // On a machine where nothing has been saved, ~/.enclave itself is missing, which also means there
    // is no file to delete.
    [Test]
    public void Delete_returns_false_when_the_directory_does_not_exist()
    {
        Assert.That(_store.Delete(CredentialsPath), Is.False);
    }

    // A path that is not fully qualified resolves against the current directory, so the CLI would
    // read a credentials.json from wherever it is run and send the token to that file's baseUrl, or
    // write the token there. The store refuses such a path whatever its caller passes. The relative
    // path here leads from the current directory to this test's own file, so a store that resolved
    // it would write or delete that file, which the checks on the disk show, and stays within this
    // test's directory.
    [Test]
    public void Every_operation_refuses_a_path_that_is_not_fully_qualified()
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, CredentialsPath);
        Assume.That(Path.IsPathFullyQualified(relativePath), Is.False, "The temporary directory is on another drive from the current directory, so no relative path leads to it.");

        Assert.Multiple(() =>
        {
            Assert.That(() => _store.WriteText(relativePath, "{}", privateToUser: true), Throws.ArgumentException);
            Assert.That(File.Exists(CredentialsPath), Is.False, "WriteText wrote the file.");
        });

        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(() => _store.ReadText(relativePath), Throws.ArgumentException);
            Assert.That(() => _store.Delete(relativePath), Throws.ArgumentException);
            Assert.That(File.Exists(CredentialsPath), Is.True, "Delete deleted the file.");
        });
    }
}
