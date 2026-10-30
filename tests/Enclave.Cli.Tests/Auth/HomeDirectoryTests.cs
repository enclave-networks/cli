using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Context;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// The CLI keeps credentials.json and cli.json in ~/.enclave, under the user's home directory
// (spec/cli-surface.md "Login, logout and status"). A user profile path that is empty or not
// fully qualified names no home directory: resolved against the current directory, it would have
// the CLI read a credentials.json from wherever it is run and send the token to that file's
// baseUrl. With no home directory the CLI reads no saved file, ENCLAVE_TOKEN and the organisation
// and partner variables work, and the commands that save or delete a file there exit 2.
public class HomeDirectoryTests
{
    private const string FileToken = "file-token-8b31e6";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    // "" is the profile path .NET gives on Linux and macOS, unless asked not to verify it, when HOME
    // names a directory that does not exist (CliHost.HomeDirectory); a relative one comes from a
    // HOME that is relative.
    private static readonly string[] Homes = [string.Empty, "home"];

    // A fully qualified path names the same directory wherever the CLI is run. Path.GetTempPath is
    // fully qualified on every OS; a literal such as /home/user is not on Windows.
    [Test]
    public void A_fully_qualified_profile_path_is_the_home_directory()
    {
        var profile = Path.Combine(Path.GetTempPath(), "home");

        Assert.That(EnclaveDirectory.HomeFrom(profile), Is.EqualTo(profile));
    }

    // Each of these resolves against the current directory or the current drive on at least one OS,
    // and none is fully qualified on any: C:home is relative to drive C's current directory on
    // Windows and a file name elsewhere, and \home is relative to the current drive on Windows and
    // a file name elsewhere.
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("home")]
    [TestCase("./home")]
    [TestCase("~")]
    [TestCase("C:home")]
    [TestCase("\\home")]
    public void A_profile_path_that_is_empty_or_not_fully_qualified_is_no_home_directory(string profile)
    {
        Assert.That(EnclaveDirectory.HomeFrom(profile), Is.Null);
    }

    // With no home directory credentials.json is not read, so ENCLAVE_TOKEN goes to the default API
    // address. The file here lies where a CLI that resolved ~/.enclave against the current directory
    // would find it, and its baseUrl is a second fake API's, so a request there, or a read of the
    // path, shows the file was used.
    [TestCaseSource(nameof(Homes))]
    public async Task With_no_home_directory_a_command_sends_ENCLAVE_TOKEN_to_the_default_api_and_reads_no_credentials_json(string home)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", OrgSystems, 200, ApiJson.Page());
        run.Home = home;
        run.SaveCredentials(FileToken, otherUrl);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        var received = LoopbackApi.Received(otherApi);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Empty, "The request went to the credentials.json in the current directory.");
            Assert.That(run.Requests.Select(request => request.Call), Is.EqualTo(new[] { $"GET {OrgSystems}" }));
            Assert.That(run.Requests.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {TestData.Token}" }));
            Assert.That(run.Files.WasRead(run.CredentialsPath), Is.False);
        });
    }

    // With no home directory and no ENCLAVE_TOKEN the CLI has no token, so it exits 3 token_missing
    // before any call, and says why the file was not read. A CLI that read the credentials.json in
    // the current directory would send that file's token to the second fake API.
    [TestCaseSource(nameof(Homes))]
    public async Task With_no_home_directory_and_no_ENCLAVE_TOKEN_a_command_exits_3_with_token_missing(string home)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", OrgSystems, 200, ApiJson.Page());
        run.Home = home;
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.SaveCredentials(FileToken, otherUrl);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        var received = LoopbackApi.Received(otherApi);

        CliAssert.Rejected(run, result, "token_missing");
        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Empty, "The request went to the credentials.json in the current directory.");
            Assert.That(run.Files.WasRead(run.CredentialsPath), Is.False);
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain("home directory"));
        });
    }

    // cli.json is not read either, so the organisation and partner come from the command line, the
    // environment or the lookup ("Context"). The cli.json here, where a CLI that resolved
    // ~/.enclave against the current directory would find it, names Globex and a partner, and the
    // token sees Acme only, so status shows the one organisation the token sees and no partner.
    [TestCaseSource(nameof(Homes))]
    public async Task With_no_home_directory_status_reads_no_cli_json(string home)
    {
        using var run = CliRun.Start();
        run.Home = home;
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Files.WriteText(run.CliConfigPath, SavedDefaults(), privateToUser: false);
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "source").GetString(), Is.EqualTo("only-organisation"));
            Assert.That(JsonAssert.Property(output, "partner").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.Files.WasRead(run.CliConfigPath), Is.False);
        });
    }

    // These commands save or delete a file in ~/.enclave, and with no home directory there is no
    // ~/.enclave, so each exits 2 invalid_argument before any call, saying so, and writes nothing.
    // A credentials.json and a cli.json lie where a CLI that resolved ~/.enclave against the
    // current directory would find them, so a run that wrote or deleted there changes them.
    [TestCaseSource(nameof(SavingRuns))]
    public async Task With_no_home_directory_a_command_that_saves_or_deletes_a_file_exits_2_and_writes_nothing(string home, string[] args)
    {
        using var run = CliRun.Start();
        run.Home = home;
        run.StdinText = TestData.Token + "\n";
        run.SaveCredentials(FileToken);
        run.Files.WriteText(run.CliConfigPath, SavedDefaults(), privateToUser: false);
        var before = Snapshot(run);
        BothOrgs.Stub(run);

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            Assert.That(Snapshot(run), Is.EqualTo(before));
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain("home directory"));
        });
    }

    private static IEnumerable<TestCaseData> SavingRuns()
    {
        string[][] commands =
        [
            ["login"],
            ["login", "--token-stdin"],
            ["logout"],
            ["org", "use", TestData.OrgName],
            ["org", "use", "--id", TestData.OrgId.ToString()],
            ["partner", "use", "--id", TestData.PartnerId.ToString()],
        ];

        foreach (var home in Homes)
        {
            foreach (var args in commands)
            {
                yield return new TestCaseData(home, args).SetArgDisplayNames($"'{home}'", string.Join(" ", args));
            }
        }
    }

    // A default organisation, Globex, and a default partner, in the form org use and partner use
    // save them.
    private static string SavedDefaults() =>
        $$"""{ "org": { "id": "{{TestData.OtherOrgId}}", "name": "{{TestData.OtherOrgName}}" }, "partner": { "id": "{{TestData.PartnerId}}" } }""";

    private static Dictionary<string, string?> Snapshot(CliRun run) =>
        run.Files.Paths.ToDictionary(path => path, path => run.Files.ReadText(path), StringComparer.Ordinal);
}
