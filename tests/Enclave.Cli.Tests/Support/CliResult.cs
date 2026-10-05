using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The exit code and captured output of one in-process CLI run.
/// </summary>
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>
    /// The whole of stdout as one JSON value. Fails the test when stdout is not JSON.
    /// </summary>
    public JsonElement StdoutJson => ParseJson(Stdout, "stdout");

    /// <summary>
    /// The non-blank lines of stdout, with any trailing carriage return removed.
    /// </summary>
    public string[] StdoutLines => Lines(Stdout);

    /// <summary>
    /// Each non-blank line of stderr parsed as JSON. Fails the test when a line is not JSON.
    /// </summary>
    public IReadOnlyList<JsonElement> StderrJson => Lines(Stderr).Select(line => ParseJson(line, "each stderr line")).ToArray();

    /// <summary>
    /// The "error" object of the one error stderr holds. Fails the test unless stderr holds exactly
    /// one non-blank line and that line is a JSON object with an "error" object.
    /// </summary>
    public JsonElement Error
    {
        get
        {
            var lines = Lines(Stderr);

            if (lines.Length != 1)
            {
                throw Failure($"Expected exactly one line on stderr, found {lines.Length}.");
            }

            var root = ParseJson(lines[0], "stderr");

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                throw Failure("Expected stderr to be a JSON object with an \"error\" object.");
            }

            return error;
        }
    }

    // Records print every public property in ToString, and the JSON properties above fail the test
    // when the output is not JSON. NUnit calls ToString to describe a value in a failure message,
    // so printing only the captured fields keeps that message from raising a second failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Describe());
        return true;
    }

    private static string[] Lines(string text) =>
        text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    private JsonElement ParseJson(string text, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw Failure($"Expected {source} to be JSON: {ex.Message}");
        }
    }

    // Assert.Fail inside Assert.Multiple records the failure and returns (NUnit 4 docs, "Multiple
    // Asserts": https://docs.nunit.org/articles/nunit/writing-tests/assertions/multiple-asserts.html).
    // The callers here have no value to return after a failure, so they throw the assertion
    // exception themselves.
    private AssertionException Failure(string message) => new($"{message}{Describe()}");

    private string Describe() =>
        $"{Environment.NewLine}Exit code: {ExitCode}{Environment.NewLine}Stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}Stderr:{Environment.NewLine}{Stderr}";
}
