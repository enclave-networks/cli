using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// A list filter given "", only white space, or a list with no entries (",") filters by nothing, so
// it exits 2 invalid_argument, keyed by the option, with the other argument checks: before the
// token is read and before any call (proposed-cli-surface.md "Details", "Errors and exit codes").
// It is never read as the filter left out: an empty value usually comes from an unset shell
// variable, and the list would then hold every item, so `system list --tag "$RETIRED" | enclave-cli
// system revoke -` would revoke every system.
//
// The run with a token answers every list and lookup a filter could lead to, so a CLI that went on
// shows a request and exits 0, or exits 2 only after a lookup call. The run without a token gives
// the same error, so a CLI that read the token first exits 3.
public class EmptyFilterTests
{
    private static readonly string[] ListSuffixes =
    [
        "systems", "unapproved-systems", "enrolment-keys", "policies", "tags", "trust-requirements", "dns/records", "dns/zones", "logs",
    ];

    [TestCase("system list", "--filter", "")]
    [TestCase("system list", "--filter", "  ")]
    [TestCase("system list", "--tag", "")]
    [TestCase("system list", "--tag", ",")]
    [TestCase("system list", "--tag", " , ")]
    [TestCase("system list", "--key", "")]
    [TestCase("system list", "--key", " ")]
    [TestCase("system list --pending", "--filter", "")]
    [TestCase("system list --pending", "--tag", ",")]
    [TestCase("system list --pending", "--key", "")]
    [TestCase("key list", "--filter", "")]
    [TestCase("key list", "--tag", "")]
    [TestCase("key list", "--tag", ",")]
    [TestCase("policy list", "--filter", " ")]
    [TestCase("policy list", "--tag", "")]
    [TestCase("policy list", "--tag", ",")]
    [TestCase("tag list", "--filter", "")]
    [TestCase("trust list", "--filter", "")]
    [TestCase("dns list-hostnames", "--filter", "")]
    [TestCase("dns list-hostnames", "--zone", "")]
    [TestCase("dns list-hostnames", "--zone", " ")]
    [TestCase("log", "--filter", "")]
    [TestCase("log", "--user", "")]
    [TestCase("log", "--user", " \t ")]
    [TestCase("log", "--level", "")]
    [TestCase("log", "--level", ",")]
    public async Task A_list_filter_given_an_empty_value_exits_2_before_the_token_and_any_call(string command, string option, string value)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var withToken = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");

        foreach (var suffix in ListSuffixes)
        {
            withToken.Stub("GET", TestData.OrgPath(suffix), json: ApiJson.Page());
        }

        string[] arguments = [.. command.Split(' '), option, value];
        var first = await withToken.RunAsync(arguments);
        var second = await withoutToken.RunAsync(arguments);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(first.Error.GetProperty("errors")), Is.EqualTo(option), first.ToString());
            Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
        });
    }
}
