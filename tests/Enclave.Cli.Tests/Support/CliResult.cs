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

    // Fails the test when stderr is not one JSON value.
    public JsonElement StderrJson => ParseJson(Stderr, "stderr");

    // Fails the test unless stdout is a JSON object whose last property is a "trace" object.
    public JsonElement StdoutTrace => TraceOf(StdoutJson, "stdout");

    public JsonElement StderrTrace => TraceOf(StderrJson, "stderr");

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

    private JsonElement TraceOf(JsonElement document, string source)
    {
        var properties = document.ValueKind == JsonValueKind.Object ? document.EnumerateObject().ToArray() : [];

        return properties is [.., { Name: "trace", Value.ValueKind: JsonValueKind.Object } last]
            ? last.Value
            : throw Failure($"Expected {source} to be a JSON object whose last property is a \"trace\" object.");
    }

    // Assert.Fail inside Assert.Multiple records the failure and returns
    // (https://docs.nunit.org/articles/nunit/writing-tests/assertions/multiple-asserts.html), and
    // the callers have no value to return after a failure.
    private AssertionException Failure(string message) => new($"{message}{Describe()}");

    private string Describe() =>
        $"{Environment.NewLine}Exit code: {ExitCode}{Environment.NewLine}Stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}Stderr:{Environment.NewLine}{Stderr}";
}
