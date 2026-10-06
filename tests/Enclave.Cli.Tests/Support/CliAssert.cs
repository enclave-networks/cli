using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Checks on a CLI run's outcome that many tests share.
/// </summary>
internal static class CliAssert
{
    /// <summary>
    /// Asserts the run exited 0.
    /// </summary>
    public static void Succeeded(CliResult result) =>
        Assert.That(result.ExitCode, Is.Zero, result.ToString());

    /// <summary>
    /// Asserts the run failed with this exit code and error code and printed nothing on stdout, and
    /// returns the error object.
    /// </summary>
    public static JsonElement Failed(CliResult result, int exitCode, string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
        });

        var error = result.Error;
        Assert.That(ErrorCode(error), Is.EqualTo(code));
        return error;
    }

    /// <summary>
    /// Asserts the CLI refused the command: this exit code and error code, nothing on stdout, and
    /// no request to the fake API.
    /// </summary>
    public static void Rejected(CliRun run, CliResult result, int exitCode = 2, string code = "invalid_argument")
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
        Assert.That(ErrorCode(result.Error), Is.EqualTo(code));
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

    // The error contract names the property "code" (AGENTS.md "CLI contract"), and agents read it
    // by that exact name, so the lookup is exact. JsonAssert.Property also matches names ignoring
    // case, which suits PATCH bodies and would let a "Code" key pass here.
    private static string? ErrorCode(JsonElement error) =>
        error.TryGetProperty("code", out var code) ? code.GetString() : null;
}
