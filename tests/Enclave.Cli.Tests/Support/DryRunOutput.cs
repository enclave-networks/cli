using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Reads what --dry-run prints, { "dryRun": true, "org": { "id", "name" }, "requests": [ { "method", "url", "body" } ] }
/// (proposed-cli-surface.md "Dry run"), failing the test when its shape is wrong.
/// </summary>
internal static class DryRunOutput
{
    // Each request's fields, sorted and joined with commas.
    private const string RequestFields = "body,method,url";

    private static readonly string[] OutputFields = ["dryRun", "org", "requests"];

    private static readonly string[] OrgFields = ["id", "name"];

    /// <summary>
    /// Checks the output's shape and returns its "org" object and the items of "requests".
    /// </summary>
    // Each request object holds method, url and body only, so no header can appear in it.
    public static (JsonElement Org, JsonElement[] Requests) Read(CliResult result)
    {
        CliAssert.Succeeded(result);

        var output = result.StdoutJson;
        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());

        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(OutputFields), result.ToString());
            Assert.That(JsonAssert.Property(output, "dryRun").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(org.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(requests.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(result.Stderr, Is.Empty);
        });

        var items = requests.EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(org), Is.EquivalentTo(OrgFields), result.ToString());
            Assert.That(items.Select(item => item.ValueKind), Is.All.EqualTo(JsonValueKind.Object), result.ToString());
        });
        Assert.That(
            items.Select(item => string.Join(",", JsonRead.PropertyNames(item).Order(StringComparer.Ordinal))),
            Is.All.EqualTo(RequestFields),
            result.ToString());

        return (org, items);
    }

    /// <summary>
    /// The output of a command that makes one call, checked as <see cref="Read"/> checks it, holding
    /// exactly one request.
    /// </summary>
    public static (JsonElement Org, JsonElement Request) ReadOne(CliResult result)
    {
        var (org, requests) = Read(result);
        Assert.That(requests, Has.Length.EqualTo(1), result.ToString());

        return (org, requests[0]);
    }

    /// <summary>
    /// The one request of a dry run in the test organisation, checked to have this method and a URL
    /// ending in this path.
    /// </summary>
    // CliRun names the organisation by ID (ENCLAVE_ORG_ID), and then no lookup gives its name, which
    // is null.
    public static JsonElement RequestInTestOrg(CliResult result, string method, string path)
    {
        var (org, request) = ReadOne(result);

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IdOf(org), Is.EqualTo(TestData.OrgId), result.ToString());
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null), result.ToString());
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(method), result.ToString());
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(path), result.ToString());
        });

        return request;
    }
}
