using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Storage;

// These are the only tests in the suite that touch the disk; every other test gives the CLI a
// MemoryFileStore. Each test works in a fresh directory under the system temporary directory, which
// stands in for the user's home directory, and TearDown deletes it, so the real home directory and
// ~/.enclave are never read or written.
public class DiskFileStoreTests
{
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
}
