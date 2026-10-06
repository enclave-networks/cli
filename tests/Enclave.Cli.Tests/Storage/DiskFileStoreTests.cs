using System.Text;
using Enclave.Cli.Storage;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Storage;

// These are the only tests in the suite that touch the disk; every other test gives the CLI a
// MemoryFileStore. Each test works in a fresh directory under the system temporary directory, which
// stands in for the user's home directory, and TearDown deletes it, so the real home directory and
// ~/.enclave are never read or written.
public class DiskFileStoreTests
{
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // 0755 and 0644: a directory and a file that group and other can read.
    private const UnixFileMode SharedDirectoryMode = PrivateDirectoryMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode SharedFileMode = PrivateFileMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const string NoModesOnWindows = "Unix file modes exist on Linux and macOS only. On Windows a file under the user profile inherits the profile's access list.";

    // U+00EB is two bytes in UTF-8, U+6771 and U+4EAC three bytes each, and U+1F511 four bytes, so
    // the text has different bytes in any encoding other than UTF-8.
    private const string NonAsciiText = "{\"name\":\"Zo\U000000EB \U00006771\U00004EAC \U0001F511\"}";

    private readonly DiskFileStore _store = new();

    private string _home;

    private string EnclaveDirectory => Path.Combine(_home, ".enclave");

    private string CredentialsPath => Path.Combine(EnclaveDirectory, "credentials.json");

    private string CliConfigPath => Path.Combine(EnclaveDirectory, "cli.json");

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

    // Enclave.Sdk.Api 1.0.4 reads credentials.json with File.ReadAllText(path)
    // (EnclaveClient.GetSettingsFile), which decodes the file as UTF-8 when it has no byte order mark
    // (dotnet/runtime release/10.0, File.cs: ReadAllText(string) passes Encoding.UTF8). Other tools
    // parse the same JSON, and RFC 8259 section 8.1 requires JSON exchanged between systems to be
    // UTF-8 and forbids adding a byte order mark, which a parser may treat as an error. Comparing the
    // bytes catches a byte order mark, which File.ReadAllText strips and a text comparison would miss.
    [Test]
    public void WriteText_writes_utf8_without_a_byte_order_mark([Values] bool privateToUser)
    {
        _store.WriteText(CredentialsPath, NonAsciiText, privateToUser);

        var expectedBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(NonAsciiText);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(CredentialsPath), Is.EqualTo(expectedBytes));
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo(NonAsciiText));
        });
    }

    // Logging in again replaces credentials.json. The new text is shorter than the old, so a write
    // over the old file that does not truncate it leaves the old text's tail behind. The directory
    // must then hold credentials.json alone: a temporary file left beside it holds a copy of the token
    // under another name, where deleting credentials.json does not remove it.
    //
    // Directory.GetFileSystemEntries(string) lists hidden entries and dot-files, since it enumerates
    // with AttributesToSkip 0 (dotnet/runtime release/10.0, EnumerationOptions.cs,
    // EnumerationOptions.Compatible).
    [Test]
    public void WriteText_replaces_an_existing_file_and_leaves_no_other_file_in_the_directory([Values] bool privateToUser)
    {
        const string OldText = "{\"personalAccessToken\":\"the-old-and-longer-token\"}";
        const string NewText = "{\"personalAccessToken\":\"new\"}";
        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, OldText);

        _store.WriteText(CredentialsPath, NewText, privateToUser);

        Assert.Multiple(() =>
        {
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo(NewText));
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

    // credentials.json holds a personal access token, which gives whoever reads it the user's access
    // to the API, so other local users must not be able to read the file or list the directory that
    // holds it. ~/.enclave is missing here, so the write creates both and sets both modes.
    [Test]
    public void Private_write_creates_a_missing_directory_as_0700_and_the_file_as_0600_on_linux_and_macos()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore(NoModesOnWindows);
            return;
        }

        _store.WriteText(CredentialsPath, "{}", privateToUser: true);

        var directoryMode = File.GetUnixFileMode(EnclaveDirectory);
        var fileMode = File.GetUnixFileMode(CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(directoryMode, Is.EqualTo(PrivateDirectoryMode));
            Assert.That(fileMode, Is.EqualTo(PrivateFileMode));
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo("{}"));
        });
    }

    // A credentials.json that others can read (made by hand, or by a tool that used the default mode)
    // is replaced with a new token at the next login, and the new token must be private whatever mode
    // the old file had. The old file's mode is set with chmod, which sets it exactly; a mode given
    // when a file is created is reduced by the umask (POSIX.1-2024 umask(),
    // https://pubs.opengroup.org/onlinepubs/9799919799/functions/umask.html).
    [Test]
    public void Private_write_over_a_0644_file_leaves_it_0600_on_linux_and_macos()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore(NoModesOnWindows);
            return;
        }

        Directory.CreateDirectory(EnclaveDirectory);
        File.WriteAllText(CredentialsPath, "{\"personalAccessToken\":\"old\"}");
        File.SetUnixFileMode(CredentialsPath, SharedFileMode);

        _store.WriteText(CredentialsPath, "{\"personalAccessToken\":\"new\"}", privateToUser: true);

        var fileMode = File.GetUnixFileMode(CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(fileMode, Is.EqualTo(PrivateFileMode));
            Assert.That(_store.ReadText(CredentialsPath), Is.EqualTo("{\"personalAccessToken\":\"new\"}"));
        });
    }

    // ~/.enclave is shared with other Enclave tools (every tool built on Enclave.Sdk.Api reads
    // credentials.json from it), and the mode of a directory that already exists was chosen by them
    // or by the user, so a private write leaves it as it is. The token stays protected because the
    // file itself is 0600. The directory's mode is set with chmod, which the umask does not reduce
    // (POSIX.1-2024 umask(), https://pubs.opengroup.org/onlinepubs/9799919799/functions/umask.html).
    [Test]
    public void Private_write_leaves_the_mode_of_an_existing_directory_unchanged_on_linux_and_macos()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore(NoModesOnWindows);
            return;
        }

        Directory.CreateDirectory(EnclaveDirectory);
        File.SetUnixFileMode(EnclaveDirectory, SharedDirectoryMode);

        _store.WriteText(CredentialsPath, "{}", privateToUser: true);

        var directoryMode = File.GetUnixFileMode(EnclaveDirectory);
        var fileMode = File.GetUnixFileMode(CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(directoryMode, Is.EqualTo(SharedDirectoryMode));
            Assert.That(fileMode, Is.EqualTo(PrivateFileMode));
        });
    }

    // cli.json holds settings with no secret and is written without privateToUser. Such a file gets
    // the mode any new file gets: .NET creates files with 0666 (DefaultCreateMode, dotnet/runtime
    // release/10.0, SafeFileHandle.Unix.cs), reduced by the umask (POSIX.1-2024 umask(),
    // https://pubs.opengroup.org/onlinepubs/9799919799/functions/umask.html). The umask is the user's
    // choice of who can read their new files, and the store overrides it only for a file that holds a
    // secret.
    //
    // The expected mode comes from a reference file that File.WriteAllText creates with that default,
    // so the test holds under any umask. Under a umask that lets group or other read new files (022
    // or 002, for example), the expected mode grants that read access, and a store that restricted
    // every file to 0600 fails the test. Under umask 077 every new file is 0600, so a store that
    // restricted every file passes. That gap is accepted: under umask 077 such a store gives the file
    // the mode the user's umask gives it anyway.
    [Test]
    public void Write_without_privateToUser_gives_the_file_the_default_mode_on_linux_and_macos()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore(NoModesOnWindows);
            return;
        }

        var referencePath = Path.Combine(_home, "reference.json");
        File.WriteAllText(referencePath, "{}");
        var defaultMode = File.GetUnixFileMode(referencePath);

        _store.WriteText(CliConfigPath, "{}", privateToUser: false);

        var fileMode = File.GetUnixFileMode(CliConfigPath);

        Assert.That(fileMode, Is.EqualTo(defaultMode), $"The umask gives new files {defaultMode}.");
    }
}
