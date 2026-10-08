using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Checks on a CLI run's outcome that many tests share.
/// </summary>
internal static class CliAssert
{
    private static readonly string[] ListFields = ["kind", "items", "total"];

    private static readonly string[] BulkFields = ["requested", "affected"];

    /// <summary>
    /// Every error code the CLI reports, with the exit code it exits with (AGENTS.md "CLI contract",
    /// proposed-cli-surface.md "Errors and exit codes").
    /// </summary>
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

    /// <summary>
    /// Asserts the run exited 0.
    /// </summary>
    public static void Succeeded(CliResult result) =>
        Assert.That(result.ExitCode, Is.Zero, result.ToString());

    /// <summary>
    /// Asserts the run failed with this error code and the exit code <see cref="ExitCodes"/> gives
    /// it, and printed nothing on stdout, and returns the error object.
    /// </summary>
    public static JsonElement Failed(CliResult result, string code) => Failed(result, ExitCode(code), code);

    /// <summary>
    /// Asserts the run failed with this exit code and error code and printed nothing on stdout, and
    /// returns the error object. Throws when the pair is not one <see cref="ExitCodes"/> holds.
    /// </summary>
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

    /// <summary>
    /// Asserts the CLI refused the command with this error code and the exit code
    /// <see cref="ExitCodes"/> gives it, printed nothing on stdout, and sent no request to the fake
    /// API.
    /// </summary>
    public static void Rejected(CliRun run, CliResult result, string code) => Rejected(run, result, ExitCode(code), code);

    /// <summary>
    /// Asserts the CLI refused the command: this exit code and error code, nothing on stdout, and
    /// no request to the fake API. Throws when the pair is not one <see cref="ExitCodes"/> holds.
    /// </summary>
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

    /// <summary>
    /// Asserts the run exited 0 and printed a list of this kind, exactly { "kind", "items", "total" }
    /// with total equal to the number of items, and returns the items array.
    /// </summary>
    // Every list command reads every page before printing (proposed-cli-surface.md "Output"), and
    // total counts the items it prints, the matches when the CLI filters them ("Filters"). A command
    // reading the list from stdin checks the kind, so the kind is compared exactly.
    public static JsonElement List(CliResult result, string kind)
    {
        if (!CliList.Kinds.Contains(kind))
        {
            throw new ArgumentException($"\"{kind}\" is not a list kind; the kinds are {string.Join(", ", CliList.Kinds)}.", nameof(kind));
        }

        Succeeded(result);
        var output = result.StdoutJson;

        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
        Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(ListFields), result.ToString());

        var items = output.GetProperty("items");
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(Text(output, "kind"), Is.EqualTo(kind), result.ToString());
            Assert.That(Integer(output, "total"), Is.EqualTo(items.GetArrayLength()), result.ToString());
        });

        return items;
    }

    /// <summary>
    /// Asserts the run exited 0 and printed exactly { "requested": <paramref name="requested"/>,
    /// "affected": <paramref name="affected"/> }, the output of a command that takes several items.
    /// </summary>
    public static void Bulk(CliResult result, int requested, int affected)
    {
        Succeeded(result);
        var output = result.StdoutJson;

        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
        Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(BulkFields), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(Integer(output, "requested"), Is.EqualTo(requested), result.ToString());
            Assert.That(Integer(output, "affected"), Is.EqualTo(affected), result.ToString());
        });
    }

    /// <summary>
    /// Runs the command, asserts it exits 0 having sent exactly one request with this method and
    /// path, and returns that request.
    /// </summary>
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

    /// <summary>
    /// Asserts the rejected command exits 2 with invalid_argument and sends nothing, then that the
    /// corrected command, run in the same sandbox, succeeds with one request with this method and
    /// path.
    /// </summary>
    // A parse error also exits 2 with invalid_argument and sends nothing, and an unknown command is
    // a parse error, so a rejection alone does not show that the command checked the input. The
    // corrected run proves the command exists and that the rejection withheld its request.
    public static async Task<RecordedRequest> RejectedThenAcceptedAsync(CliRun run, string[] rejected, string[] corrected, string method, string path)
    {
        Rejected(run, await run.RunAsync(rejected));

        return await AcceptedAsync(run, method, path, corrected);
    }

    // A code outside the contract's set, or an exit code the contract does not give that code, is a
    // mistake in the test, which no CLI behaviour could satisfy, so it is reported as one.
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

    // The error contract names the property "code" (AGENTS.md "CLI contract"), and agents read it
    // by that exact name, so the lookup is exact. JsonAssert.Property also matches names ignoring
    // case, which suits PATCH bodies and would let a "Code" key pass here.
    private static string? ErrorCode(JsonElement error) => Text(error, "code");

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
