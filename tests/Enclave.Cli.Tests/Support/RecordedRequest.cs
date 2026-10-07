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

    /// <summary>
    /// The page a list request asks for: its page query parameter, or 0 when it has none, which the
    /// API reads as page 0 (portal PaginatedRequestModel.Page). Fails the test when the parameter is
    /// not a whole number.
    /// </summary>
    public int PageNumber
    {
        get
        {
            var page = QueryValue("page");

            if (page is null)
            {
                return 0;
            }

            return int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new AssertionException($"Expected the page parameter of {Method} {Path} to be a whole number, found \"{page}\".");
        }
    }

    /// <summary>
    /// The IDs in an array property of the JSON body, such as a bulk call's systemIds, each as text:
    /// a string as it is, and a number, which is how typed integer IDs are written, in invariant
    /// form. Fails the test when the property is missing or is not an array of strings and integers.
    /// </summary>
    public string[] BodyIds(string field)
    {
        var array = JsonAssert.Property(BodyJson, field);

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected \"{field}\" in the body of {Method} {Path} to be an array, found {array.ValueKind}: {Body}");
        }

        return array.EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString()!,
                JsonValueKind.Number when item.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                _ => throw new AssertionException($"Expected \"{field}\" in the body of {Method} {Path} to hold strings and integers, found {item.GetRawText()}: {Body}"),
            })
            .ToArray();
    }

    // Records print every public property in ToString, and BodyJson fails the test when the body is
    // not JSON. NUnit calls ToString to describe a value in a failure message, so printing only the
    // captured fields keeps that message from raising a second failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Method = {Method}, Path = {Path}, Query = {{ {string.Join(", ", Query.Select(pair => $"{pair.Key}={pair.Value}"))} }}, Body = {Body}, Authorization = {Authorization}");
        return true;
    }
}
