using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

// CA1064 wants public exceptions so callers outside the assembly can catch them. CliAction catches
// this one inside the assembly, and CA1515 keeps the CLI's types internal.
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Caught and reported inside the CLI; never crosses the assembly boundary.")]
internal sealed class CliException : Exception
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoFieldErrors =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public CliException(
        ErrorCode code,
        string detail,
        int? status = null,
        string? title = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null,
        Exception? innerException = null)
        : base(detail, innerException)
    {
        ArgumentNullException.ThrowIfNull(code);

        Code = code;
        Detail = detail;
        Status = status;
        Title = title ?? code.Title;
        Errors = errors ?? NoFieldErrors;
    }

    public ErrorCode Code { get; }

    // Null for an error the CLI found.
    public int? Status { get; }

    public string Title { get; }

    public string Detail { get; }

    // Keyed by the option, argument or API field each message is about, as in the API's problem
    // details.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }

    // The items a name matched, or the organisations a token sees for no_org; otherwise null.
    public JsonArray? Candidates { get; init; }

    // The IDs sent in the bulk calls that succeeded before this error, or null outside bulk commands.
    public int? Requested { get; init; }

    // The items those bulk calls changed, or null outside bulk commands.
    public int? Affected { get; init; }

    public CliException WithBulkCounts(int requested, int affected) =>
        new(Code, Detail, Status, Title, Errors, InnerException)
        {
            Candidates = Candidates,
            Requested = requested,
            Affected = affected,
        };
}
