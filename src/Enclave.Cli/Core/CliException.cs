using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

/// <summary>
/// An error the CLI reports as one JSON object on stderr, exiting with its code's exit code.
/// Command code throws it; the command action catches it and writes it.
/// </summary>
// CA1064 asks for public exception types so that callers outside the assembly can catch them. This
// one never leaves the assembly: CliAction catches every exception a command throws and turns it
// into the stderr error and exit code, and CA1515 keeps the CLI's types internal.
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

    /// <summary>
    /// The HTTP status of the API response the error came from, or null for an error the CLI found.
    /// </summary>
    public int? Status { get; }

    public string Title { get; }

    public string Detail { get; }

    /// <summary>
    /// Messages keyed by the option, argument or API field they are about, in the API's problem
    /// details shape.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }

    /// <summary>
    /// The items a name matched, or the organisations a token sees for no_org; null when the error
    /// is not about a choice between items.
    /// </summary>
    public JsonArray? Candidates { get; init; }

    /// <summary>
    /// The IDs sent in the bulk calls that succeeded before this error, or null outside bulk commands.
    /// </summary>
    public int? Requested { get; init; }

    /// <summary>
    /// The items those bulk calls changed, or null outside bulk commands.
    /// </summary>
    public int? Affected { get; init; }

    /// <summary>
    /// The same error with the counts of the bulk calls that succeeded before it.
    /// </summary>
    public CliException WithBulkCounts(int requested, int affected) =>
        new(Code, Detail, Status, Title, Errors, InnerException)
        {
            Candidates = Candidates,
            Requested = requested,
            Affected = affected,
        };
}
