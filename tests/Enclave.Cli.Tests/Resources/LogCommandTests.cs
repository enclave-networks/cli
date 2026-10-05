using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class LogCommandTests
{
    [Test]
    public async Task Log_list_gets_one_page_of_organisation_logs_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("logs"), json: ApiJson.Page(ApiJson.Log("System enrolled"), ApiJson.Log("Policy changed")));

        var result = await run.RunAsync("log", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("logs")));
            Assert.That(QueryValue(request, "per_page"), Is.EqualTo("100"));
            Assert.That(StringField(JsonAssert.Property(output, "items"), "message"), Is.EqualTo("System enrolled|Policy changed"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    // A limit of at most 200 fits one API page, so the CLI asks the API for that many and makes
    // one call (proposal, "Calls per command": the API returns at most 200 per page).
    [Test]
    public async Task Log_list_with_a_limit_asks_the_api_for_a_page_of_that_size()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("logs"), json: ApiJson.Page(ApiJson.Log("System enrolled")));

        var result = await run.RunAsync("log", "list", "--limit", "20");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("logs")));
            Assert.That(QueryValue(request, "per_page"), Is.EqualTo("20"));
        });
    }

    // The API's log list takes page and per_page only (Enclave.Sdk.Api 1.0.4,
    // LogsClient.GetLogsAsync), and the proposal gives log list no options of its own. --dry-run
    // and --yes exist only on commands that change something.
    //
    // An unknown command also exits 2 without a request, so the test then runs log list without the
    // option in the same sandbox and checks it reaches the API. That proves the command exists and
    // the rejection came from the option.
    [TestCase("log list --search enrolled")]
    [TestCase("log list --sort RecentlyCreated")]
    [TestCase("log list --yes")]
    [TestCase("log list --dry-run")]
    public async Task Log_list_rejects_options_it_does_not_have_without_sending_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("logs"), json: ApiJson.Page(ApiJson.Log("System enrolled")));

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.Stderr);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
        });

        var accepted = await run.RunAsync("log", "list");

        Assert.That(accepted.ExitCode, Is.Zero, accepted.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("logs")));
        });
    }

    private static string? QueryValue(RecordedRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value : null;

    // Log messages can hold commas, so the list is joined with a bar; a joined string keeps
    // constant arrays out of the assertions (CA1861) and prints readably on failure.
    private static string StringField(JsonElement array, string name) =>
        string.Join("|", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetString()));
}
