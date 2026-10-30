using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    // Fails the test when stdout is not one JSON value.
    public JsonElement StdoutJson => ParseJson(Stdout, "stdout");

    // The non-blank lines of stdout, with any trailing carriage return removed.
    public string[] StdoutLines => Lines(Stdout);

    // The non-blank lines of stderr before the error, each a diagnostic. A diagnostic doesn't start
    // with "{", so the error starts at the first line that does ("Details").
    public string[] Diagnostics => Lines(Stderr[..ErrorStart]);

    // stderr after its diagnostics: the error, or empty when there is none.
    public string ErrorText => Stderr[ErrorStart..];

    // The one JSON value stderr holds after its diagnostics. Fails the test unless there is exactly
    // one.
    public JsonElement StderrJson => ParseJson(ErrorText, "stderr after its diagnostics");

    // The "error" object of the one error stderr holds. Fails the test unless that error is a JSON
    // object with an "error" object.
    public JsonElement Error
    {
        get
        {
            var root = StderrJson;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                throw Failure("Expected stderr to be a JSON object with an \"error\" object.");
            }

            return error;
        }
    }

    // A record's ToString prints every public property, and the JSON properties above fail the test
    // when the output is not JSON, so a failure message NUnit builds with ToString would raise a
    // second failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Describe());
        return true;
    }

    private int ErrorStart
    {
        get
        {
            var start = 0;

            while (start < Stderr.Length && Stderr[start] != '{')
            {
                var end = Stderr.IndexOf('\n', start);
                start = end < 0 ? Stderr.Length : end + 1;
            }

            return start;
        }
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

    // Assert.Fail inside Assert.Multiple records the failure and returns
    // (https://docs.nunit.org/articles/nunit/writing-tests/assertions/multiple-asserts.html), and
    // the callers have no value to return after a failure.
    private AssertionException Failure(string message) => new($"{message}{Describe()}");

    private string Describe() =>
        $"{Environment.NewLine}Exit code: {ExitCode}{Environment.NewLine}Stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}Stderr:{Environment.NewLine}{Stderr}";
}
