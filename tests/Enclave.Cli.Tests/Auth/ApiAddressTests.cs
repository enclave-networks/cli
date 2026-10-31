using System.Text.RegularExpressions;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Options on every command", "Environments". Production's addresses are the
// run's own fakes and staging's are second fakes, so the fake a request reached shows the
// environment.
public class ApiAddressTests
{
    private const string EnvironmentVariable = "ENCLAVE_CLI_ENV";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string Customers = TestData.PartnerPath("customers");

    private static readonly string[] OrgSystemsOnly = [$"GET {OrgSystems}"];

    private static readonly string[] CustomersOnly = [$"GET {Customers}"];

    private static readonly string[] NamedVariables = ["ENCLAVE_CLI_ORG", "ENCLAVE_CLI_ORG_ID", "ENCLAVE_CLI_PARTNER_ID"];

    // Every variable the CLI read starts ENCLAVE_CLI_ ("Options on every command"); the old names are
    // set to values that would change the outcome.
    private static readonly string[][] CommandsThatReadTheEnvironment =
    [
        ["system", "list"],
        ["status"],
        ["partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString()],
        ["org", "use", TestData.OrgName],
        ["login"],
        ["logout"],
        ["commands"],
    ];

    // An empty variable counts as unset.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("production")]
    [TestCase("PRODUCTION")]
    public async Task ENCLAVE_CLI_PAT_goes_to_production_s_addresses_unless_ENCLAVE_CLI_ENV_names_staging(string? environment)
    {
        using var run = CliRun.Start();
        using var staging = StagingFakes.Start(run);
        run.Environment[EnvironmentVariable] = environment;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", Customers, json: ApiJson.Page());

        var systems = await run.RunAsync("system", "list");
        var customers = await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(systems);
        CliAssert.Succeeded(customers);
        Assert.Multiple(() =>
        {
            Assert.That(run.ApiRequests.Select(request => request.Call), Is.EqualTo(OrgSystemsOnly));
            Assert.That(run.PartnerApiRequests.Select(request => request.Call), Is.EqualTo(CustomersOnly));
            Assert.That(staging.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(systems.StdoutTrace, "api").GetString(), Is.EqualTo(run.ApiUrl.AbsoluteUri));
            Assert.That(JsonAssert.Property(customers.StdoutTrace, "partnerApi").GetString(), Is.EqualTo(run.PartnerApiUrl.AbsoluteUri));
        });
    }

    // The trace's api and partnerApi show which environment a command reached ("Environments").
    [TestCase("staging")]
    [TestCase("Staging")]
    public async Task ENCLAVE_CLI_PAT_goes_to_staging_s_addresses_with_ENCLAVE_CLI_ENV_staging(string environment)
    {
        using var run = CliRun.Start();
        using var staging = StagingFakes.Start(run);
        run.Environment[EnvironmentVariable] = environment;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", Customers, json: ApiJson.Page());
        LoopbackApi.Stub(staging.Api, "GET", OrgSystems, 200, ApiJson.Page());
        LoopbackApi.Stub(staging.PartnerApi, "GET", Customers, 200, ApiJson.Page());

        var systems = await run.RunAsync("system", "list");
        var customers = await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(systems);
        CliAssert.Succeeded(customers);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "A request went to production's address.");
            Assert.That(LoopbackApi.Received(staging.Api).Select(request => request.Call), Is.EqualTo(OrgSystemsOnly));
            Assert.That(LoopbackApi.Received(staging.PartnerApi).Select(request => request.Call), Is.EqualTo(CustomersOnly));
            Assert.That(staging.Requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(JsonAssert.Property(systems.StdoutTrace, "api").GetString(), Is.EqualTo(run.StagingApiUrl.AbsoluteUri));
            Assert.That(JsonAssert.Property(customers.StdoutTrace, "partnerApi").GetString(), Is.EqualTo(run.StagingPartnerApiUrl.AbsoluteUri));
        });
    }

    // Staging is hidden, so the error lists neither value; the value could be a token set in the
    // wrong variable, so the error does not repeat it.
    [TestCase("prod")]
    [TestCase("dev")]
    [TestCase(" staging")]
    [TestCase("staging-api.enclave.io")]
    public async Task ENCLAVE_CLI_ENV_set_to_another_value_exits_2_before_any_call_without_listing_the_values(string environment)
    {
        using var run = CliRun.Start();
        run.Environment[EnvironmentVariable] = environment;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(result.Error, EnvironmentVariable);
            Assert.That(result.Stderr, Does.Not.Contain("staging").IgnoreCase.And.Not.Contain("production").IgnoreCase);
        });
    }

    // ENCLAVE_CLI_ENV applies only to ENCLAVE_CLI_PAT, so the saved login's token is not sent to an
    // environment the caller did not ask for. login asks nothing.
    [TestCase("staging", "system", "list")]
    [TestCase("production", "system", "list")]
    [TestCase("staging", "status")]
    [TestCase("staging", "login")]
    public async Task ENCLAVE_CLI_ENV_without_ENCLAVE_CLI_PAT_exits_2_before_any_call(string environment, params string[] args)
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        run.AtTerminal();
        run.AnswerLogin(TestData.Token);
        run.Environment[EnvironmentVariable] = environment;
        BothOrgs.Stub(run);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(result.Error, EnvironmentVariable);
            Assert.That(run.Terminal.Output, Is.Empty);
        });
    }

    [Test]
    public async Task An_empty_ENCLAVE_CLI_ENV_without_ENCLAVE_CLI_PAT_counts_as_unset()
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        run.Environment[EnvironmentVariable] = string.Empty;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        await CliAssert.AcceptedAsync(run, "GET", OrgSystems, "system", "list");
    }

    // Staging is for Enclave's own staff, so help and the command catalogue name neither it nor the
    // option and variable that reach it ("Environments").
    [Test]
    public async Task No_help_or_commands_output_names_staging()
    {
        var outputs = await HelpAndCommandsOutputAsync();

        Assert.That(outputs.Where(output => output.Contains("staging", StringComparison.OrdinalIgnoreCase) || output.Contains(EnvironmentVariable, StringComparison.Ordinal)), Is.Empty);
    }

    // The option descriptions name the variables an option overrides.
    [Test]
    public async Task Help_names_only_variables_the_cli_reads()
    {
        var outputs = await HelpAndCommandsOutputAsync();

        var named = outputs.SelectMany(output => Regex.Matches(output, "ENCLAVE_[A-Z_]+").Select(match => match.Value)).Distinct().Order(StringComparer.Ordinal);

        Assert.That(named, Is.EqualTo(NamedVariables));
    }

    // No error names staging unless the command line or environment gave it; a slip near --staging
    // is not answered with it.
    [TestCase("login", "--stagin")]
    [TestCase("login", "--stage")]
    [TestCase("login", "--production")]
    [TestCase("login")]
    [TestCase("system", "list")]
    [TestCase("org", "use", TestData.OrgName)]
    public async Task An_error_names_staging_only_when_it_was_given(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero, result.ToString());
            Assert.That(result.Stderr, Does.Not.Contain("staging").IgnoreCase.And.Not.Contain(EnvironmentVariable));
        });
    }

    // The old names are set as an earlier release read them, so a CLI that read one would take its
    // token, organisation or address.
    [TestCaseSource(nameof(CommandsThatReadTheEnvironment))]
    public async Task Every_variable_the_cli_reads_starts_with_ENCLAVE_CLI(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        run.Environment.Remove("ENCLAVE_CLI_PAT");
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Environment["ENCLAVE_TOKEN"] = TestData.Token;
        run.Environment["ENCLAVE_ORG_ID"] = TestData.OrgId.ToString();
        run.Environment["ENCLAVE_API_URL"] = otherUrl;
        run.Environment["ENCLAVE_PARTNER_API_URL"] = otherUrl;
        run.StdinIsTerminal = true;
        run.StderrIsTerminal = true;

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(run.VariablesRead, Is.All.StartsWith("ENCLAVE_CLI_"), result.ToString());
            Assert.That(run.Requests, Is.Empty, result.ToString());
            Assert.That(LoopbackApi.Received(otherApi), Is.Empty, result.ToString());
        });
    }

    // commands, the root's help and every command's help.
    private static async Task<List<string>> HelpAndCommandsOutputAsync()
    {
        using var run = CliRun.Start();

        var commands = await run.RunAsync("commands");

        CliAssert.Succeeded(commands);
        var outputs = new List<string> { commands.Stdout, (await run.RunAsync("--help")).Stdout };

        foreach (var entry in commands.StdoutJson.GetProperty("commands").EnumerateArray())
        {
            var help = await run.RunAsync([.. entry.GetProperty("command").GetString()!.Split(' '), "--help"]);
            CliAssert.Succeeded(help);
            outputs.Add(help.Stdout);
        }

        return outputs;
    }
}
