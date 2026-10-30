using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal static class CliAssert
{
    private static readonly string[] ListFields = ["kind", "items", "total"];

    private static readonly string[] BulkFields = ["requested", "affected"];

    // Every error code the CLI reports, with its exit code (spec/cli-surface.md "Errors and exit
    // codes").
    public static IReadOnlyDictionary<string, int> ExitCodes { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["api_error"] = 1,
        ["not_implemented"] = 1,
        ["invalid_argument"] = 2,
        ["no_org"] = 2,
        ["no_partner"] = 2,
        ["token_missing"] = 3,
        ["token_invalid"] = 3,
        ["forbidden"] = 4,
        ["not_found"] = 5,
        ["transient"] = 6,
    };

    public static void Succeeded(CliResult result) =>
        Assert.That(result.ExitCode, Is.Zero, result.ToString());

    // Asserts the run failed with this error code and its exit code, with nothing on stdout, and
    // returns the error object.
    public static JsonElement Failed(CliResult result, string code) => Failed(result, ExitCode(code), code);

    // Asserts the run failed with this exit code and error code, with nothing on stdout, and
    // returns the error object.
    public static JsonElement Failed(CliResult result, int exitCode, string code)
    {
        CheckExitCode(exitCode, code);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
        });

        var error = result.Error;
        Assert.That(ErrorCode(error), Is.EqualTo(code), result.ToString());
        return error;
    }

    // Asserts the CLI refused the command with this error code and its exit code, with nothing on
    // stdout and no request sent.
    public static void Rejected(CliRun run, CliResult result, string code) => Rejected(run, result, ExitCode(code), code);

    // Asserts the CLI refused the command with this exit code and error code, with nothing on
    // stdout and no request sent.
    public static void Rejected(CliRun run, CliResult result, int exitCode = 2, string code = "invalid_argument")
    {
        CheckExitCode(exitCode, code);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
        Assert.That(ErrorCode(result.Error), Is.EqualTo(code), result.ToString());
    }

    // Asserts the error reports a problem with each of these options or arguments and no other.
    // detail names a single problem, and errors stays empty since it would only repeat detail;
    // errors keys several (spec/cli-surface.md "Errors and exit codes").
    public static void Problems(JsonElement error, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var errors = JsonRead.PropertyNames(JsonAssert.Property(error, "errors"));

        if (keys.Length == 1)
        {
            Assert.That(JsonAssert.Property(error, "detail").GetString(), Does.Contain(keys[0]), error.ToString());
            Assert.That(errors, Is.Empty, error.ToString());
        }
        else
        {
            Assert.That(errors, Is.EquivalentTo(keys), error.ToString());
        }
    }

    // Asserts the run exited 0 and printed exactly { "kind", "items", "total" }, and any trace after
    // them, with this kind and total equal to the number of items, and returns the items.
    // total counts the items printed, the matches when the CLI filters them (spec/cli-surface.md
    // "Filters").
    public static JsonElement List(CliResult result, string kind)
    {
        if (!CliList.Kinds.Contains(kind))
        {
            throw new ArgumentException($"\"{kind}\" is not a list kind; the kinds are {string.Join(", ", CliList.Kinds)}.", nameof(kind));
        }

        Succeeded(result);
        var output = result.StdoutJson;

        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
        Assert.That(JsonRead.PropertyNamesBeforeTrace(output), Is.EquivalentTo(ListFields), result.ToString());

        var items = output.GetProperty("items");
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(Text(output, "kind"), Is.EqualTo(kind), result.ToString());
            Assert.That(Integer(output, "total"), Is.EqualTo(items.GetArrayLength()), result.ToString());
        });

        return items;
    }

    // Asserts the run exited 0 and printed exactly
    // { "requested": requested, "affected": affected }, and any trace after them.
    public static void Bulk(CliResult result, int requested, int affected)
    {
        Succeeded(result);
        var output = result.StdoutJson;

        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
        Assert.That(JsonRead.PropertyNamesBeforeTrace(output), Is.EquivalentTo(BulkFields), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(Integer(output, "requested"), Is.EqualTo(requested), result.ToString());
            Assert.That(Integer(output, "affected"), Is.EqualTo(affected), result.ToString());
        });
    }

    // Runs the command, asserts it exits 0 having sent exactly one request with this method and
    // path, and returns that request.
    public static async Task<RecordedRequest> AcceptedAsync(CliRun run, string method, string path, params string[] args)
    {
        var result = await run.RunAsync(args);

        Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
        return request;
    }

    // Asserts the rejected command exits 2 with invalid_argument and sends nothing, then that the
    // corrected command succeeds with one request with this method and path.
    // A parse error, such as an unknown command, also exits 2 with invalid_argument and sends
    // nothing, so the corrected run is what proves the command checked the input.
    public static async Task<RecordedRequest> RejectedThenAcceptedAsync(CliRun run, string[] rejected, string[] corrected, string method, string path)
    {
        Rejected(run, await run.RunAsync(rejected));

        return await AcceptedAsync(run, method, path, corrected);
    }

    // A code or exit code outside the contract is a mistake in the test, which no CLI behaviour
    // could satisfy, so it throws ArgumentException.
    private static int ExitCode(string code) =>
        ExitCodes.TryGetValue(code, out var exitCode)
            ? exitCode
            : throw new ArgumentException($"\"{code}\" is not an error code the CLI reports; the codes are {string.Join(", ", ExitCodes.Keys)}.", nameof(code));

    private static void CheckExitCode(int exitCode, string code)
    {
        if (ExitCode(code) != exitCode)
        {
            throw new ArgumentException($"The CLI exits {ExitCodes[code]} with {code}, not {exitCode}.", nameof(exitCode));
        }
    }

    // Agents read "code" by its exact name. JsonAssert.Property also matches names ignoring case,
    // which would let "Code" pass.
    private static string? ErrorCode(JsonElement error) => Text(error, "code");

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
