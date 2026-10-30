using System.Text.Json.Nodes;
using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Storage;

// The only tests that touch the disk. Each works in a fresh temporary directory that stands in for
// the home directory, so the real ~/.enclave is never read or written.
public class DiskFileStoreTests
{
    private const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode ReadableByOthers = UserOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryReadableByOthers =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly string EmptyObject = new JsonObject().ToJsonString();

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

    // Commands work without credentials.json and cli.json, so a missing file is ordinary.
    [Test]
    public void ReadText_returns_null_when_the_file_does_not_exist()
    {
        Directory.CreateDirectory(EnclaveDirectory);

        Assert.That(_store.ReadText(CredentialsPath), Is.Null);
    }

    // ~/.enclave is missing where nothing has been saved, and .NET reports that with
    // DirectoryNotFoundException
    // (https://learn.microsoft.com/dotnet/api/system.io.file.readalltext, .NET 10).
    [Test]
    public void ReadText_returns_null_when_the_directory_does_not_exist()
    {
        Assert.That(_store.ReadText(CredentialsPath), Is.Null);
    }

    // login runs where ~/.enclave does not exist yet.
    [Test]
    public void WriteText_creates_the_missing_directory_and_the_file([Values] bool privateToUser)
    {
        _store.WriteText(CredentialsPath, EmptyObject, privateToUser);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(EnclaveDirectory), Is.True, "The directory was not created.");
            Assert.That(File.Exists(CredentialsPath), Is.True, "The file was not created.");
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo(EmptyObject));
        });
    }

    // ~/.enclave holds credentials.json, so it is 0700 whichever write creates it: `org use` before
    // `login` creates it for cli.json, which is not private (spec/cli-surface.md "Login, logout and
    // status").
    [Test]
    public void WriteText_creates_a_missing_directory_readable_by_the_user_only([Values] bool privateToUser)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        _store.WriteText(Path.Combine(EnclaveDirectory, "cli.json"), EmptyObject, privateToUser);

        Assert.That(File.GetUnixFileMode(EnclaveDirectory), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    // ~/.enclave is shared with other Enclave tools, so the CLI sets the mode only of a directory it
    // creates (spec/cli-surface.md "Login, logout and status").
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

        _store.WriteText(CredentialsPath, EmptyObject, privateToUser);

        Assert.That(File.GetUnixFileMode(EnclaveDirectory), Is.EqualTo(DirectoryReadableByOthers));
    }

    // .NET creates a file 0666 less the umask, usually 0644, unless given a mode
    // (FileStreamOptions.UnixCreateMode, .NET 10).
    [Test]
    public void WriteText_private_to_the_user_gives_the_file_mode_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        _store.WriteText(CredentialsPath, EmptyObject, privateToUser: true);

        Assert.That(File.GetUnixFileMode(CredentialsPath), Is.EqualTo(UserOnly));
    }

    // A rewrite that kept the old file's mode would leave the new token readable by others.
    [Test]
    public void WriteText_private_to_the_user_over_a_file_readable_by_others_gives_mode_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no Unix file modes.");
            return;
        }

        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, Credentials("old"));
        File.SetUnixFileMode(CredentialsPath, ReadableByOthers);

        _store.WriteText(CredentialsPath, EmptyObject, privateToUser: true);

        var mode = File.GetUnixFileMode(CredentialsPath);
        Assert.Multiple(() =>
        {
            Assert.That(mode, Is.EqualTo(UserOnly));
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo(EmptyObject));
        });
    }

    // Nothing of a longer old file is left after the new text, and no other file is left beside it.
    [Test]
    public void WriteText_replaces_the_text_of_an_existing_file([Values] bool privateToUser)
    {
        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, Credentials("a much longer old token"));

        _store.WriteText(CredentialsPath, EmptyObject, privateToUser);

        Assert.Multiple(() =>
        {
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo(EmptyObject));
            Assert.That(Directory.GetFileSystemEntries(EnclaveDirectory), Is.EqualTo(new[] { CredentialsPath }));
        });
    }

    // logout reports in its "deleted" field whether there was a file to delete.
    [Test]
    public void Delete_removes_the_file_and_returns_true()
    {
        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, EmptyObject);

        var deleted = _store.Delete(CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(File.Exists(CredentialsPath), Is.False, "The file is still on the disk.");
        });
    }

    [Test]
    public void Delete_returns_false_when_the_file_does_not_exist()
    {
        Directory.CreateDirectory(EnclaveDirectory);

        Assert.That(_store.Delete(CredentialsPath), Is.False);
    }

    // ~/.enclave is missing where nothing has been saved.
    [Test]
    public void Delete_returns_false_when_the_directory_does_not_exist()
    {
        Assert.That(_store.Delete(CredentialsPath), Is.False);
    }

    // A relative path resolves against the current directory, so the CLI would read a
    // credentials.json from wherever it runs and send the token to its baseUrl. The path here leads
    // to this test's own file, so the checks on the disk show whether the store resolved it.
    [Test]
    public void Every_operation_refuses_a_path_that_is_not_fully_qualified()
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, CredentialsPath);
        Assume.That(Path.IsPathFullyQualified(relativePath), Is.False, "The temporary directory is on another drive from the current directory, so no relative path leads to it.");

        Assert.Multiple(() =>
        {
            Assert.That(() => _store.WriteText(relativePath, EmptyObject, privateToUser: true), Throws.ArgumentException);
            Assert.That(File.Exists(CredentialsPath), Is.False, "WriteText wrote the file.");
        });

        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, EmptyObject);

        Assert.Multiple(() =>
        {
            Assert.That(() => _store.ReadText(relativePath), Throws.ArgumentException);
            Assert.That(() => _store.Delete(relativePath), Throws.ArgumentException);
            Assert.That(File.Exists(CredentialsPath), Is.True, "Delete deleted the file.");
        });
    }

    private static string Credentials(string token) => new JsonObject { ["personalAccessToken"] = token }.ToJsonString();
}
