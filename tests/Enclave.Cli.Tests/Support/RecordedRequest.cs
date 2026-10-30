using System.Globalization;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    string? Body,
    string? Authorization)
{
    /// <summary>
    /// Fails the test when there is no body or it is not JSON.
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
                // "Multiple Asserts"), and there is no value to return.
                throw new AssertionException($"Expected the body of {Method} {Path} to be JSON: {ex.Message}{Environment.NewLine}{Body}");
            }
        }
    }

    /// <summary>
    /// "METHOD path", the form CliRun.Calls lists requests in.
    /// </summary>
    public string Call => $"{Method} {Path}";

    public string? QueryValue(string name) => Query.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// The page query parameter, or 0 when there is none, as the API reads it (portal
    /// PaginatedRequestModel.Page). Fails the test when it is not a whole number.
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
    /// The IDs in an array property of the JSON body, each as text; typed integer IDs are written
    /// as numbers. Fails the test when the property is not an array of strings and integers.
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

    /// <summary>
    /// The IDs <see cref="BodyIds"/> reads, sorted and joined with commas.
    /// </summary>
    // The order of a bulk call's IDs or names does not change what it does.
    public string SortedBodyIds(string field) => string.Join(",", BodyIds(field).Order(StringComparer.Ordinal));

    // A record's ToString prints every public property, and BodyJson fails the test when the body
    // is not JSON, so a failure message NUnit builds with ToString would raise a second failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Method = {Method}, Path = {Path}, Query = {{ {string.Join(", ", Query.Select(pair => $"{pair.Key}={pair.Value}"))} }}, Body = {Body}, Authorization = {Authorization}");
        return true;
    }
}
