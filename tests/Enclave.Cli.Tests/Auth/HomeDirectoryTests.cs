using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Context;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// A profile path that is empty or not fully qualified names no home directory: resolved against
// the current directory, it would have the CLI read a credentials.json from wherever it is run and
// send the token to that file's baseUrl.
public class HomeDirectoryTests
{
    private const string FileToken = "file-token-8b31e6";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    // .NET gives "" on Linux and macOS when HOME names a directory that does not exist
    // (CliHost.HomeDirectory), and a relative path for a relative HOME.
    private static readonly string[] Homes = [string.Empty, "home"];

    // Path.GetTempPath is fully qualified on every OS; a literal such as /home/user is not on
    // Windows.
    [Test]
    public void A_fully_qualified_profile_path_is_the_home_directory()
    {
        var profile = Path.Combine(Path.GetTempPath(), "home");

        Assert.That(EnclaveDirectory.HomeFrom(profile), Is.EqualTo(profile));
    }

    // On Windows C:home is relative to drive C's current directory and \home to the current drive;
    // elsewhere each is a file name.
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

    // The file lies where a CLI that resolved ~/.enclave against the current directory would find
    // it, and its baseUrl is a second fake API's.
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

    // The error says why the file was not read. A CLI that read the credentials.json in the current
    // directory would send its token to the second fake API.
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

    // The cli.json, where a CLI that resolved ~/.enclave against the current directory would find
    // it, names Globex and a partner; the token sees Acme only.
    [TestCaseSource(nameof(Homes))]
    public async Task With_no_home_directory_status_reads_no_cli_json(string home)
    {
        using var run = CliRun.Start();
        run.Home = home;
        run.Environment.Remove("ENCLAVE_ORG_ID");
        SaveDefaults(run);
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

    // The error says why. A credentials.json and a cli.json lie where a CLI that resolved ~/.enclave
    // against the current directory would change them.
    [TestCaseSource(nameof(SavingRuns))]
    public async Task With_no_home_directory_a_command_that_saves_or_deletes_a_file_exits_2_and_writes_nothing(string home, string[] args)
    {
        using var run = CliRun.Start();
        run.Home = home;
        run.StdinText = TestData.Token + "\n";
        run.SaveCredentials(FileToken);
        SaveDefaults(run);
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

    // Globex and a partner, as org use and partner use save them.
    private static void SaveDefaults(CliRun run) =>
        run.SaveDefaults(TestData.OtherOrgId.ToString(), TestData.OtherOrgName, TestData.PartnerId.ToString());

    private static Dictionary<string, string?> Snapshot(CliRun run) =>
        run.Files.Paths.ToDictionary(path => path, path => run.Files.ReadText(path), StringComparer.Ordinal);
}
