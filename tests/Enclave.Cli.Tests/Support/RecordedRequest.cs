using System.Globalization;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// One request the fake API received.
/// </summary>
internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    string? Body,
    string? Authorization)
{
    /// <summary>
    /// The request body parsed as JSON. Fails the test when there is no body or it is not JSON.
    /// </summary>
    public JsonElement BodyJson
    {
        get
        {
            if (Body is null)
            {
                throw new AssertionException($"Expected a JSON body on {Method} {Path}, found none.");
            }

            try
            {
                using var document = JsonDocument.Parse(Body);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                // Assert.Fail inside Assert.Multiple records the failure and returns (NUnit 4 docs,
                // "Multiple Asserts"), and there is no value to return here, so the assertion
                // exception is thrown directly.
                throw new AssertionException($"Expected the body of {Method} {Path} to be JSON: {ex.Message}{Environment.NewLine}{Body}");
            }
        }
    }

    /// <summary>
    /// The value of a query parameter, or null when the request does not carry it.
    /// </summary>
    public string? QueryValue(string name) => Query.TryGetValue(name, out var value) ? value : null;

    // Records print every public property in ToString, and BodyJson fails the test when the body is
    // not JSON. NUnit calls ToString to describe a value in a failure message, so printing only the
    // captured fields keeps that message from raising a second failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Method = {Method}, Path = {Path}, Query = {{ {string.Join(", ", Query.Select(pair => $"{pair.Key}={pair.Value}"))} }}, Body = {Body}, Authorization = {Authorization}");
        return true;
    }
}
