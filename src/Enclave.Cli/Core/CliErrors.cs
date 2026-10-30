using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

internal static class CliErrors
{
    /// <summary>
    /// <paramref name="key"/> is an option or argument name, such as "--id".
    /// </summary>
    public static CliException InvalidArgument(string key, string message) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message));

    /// <summary>
    /// Exit 2 for a problem no single option or argument owns.
    /// </summary>
    public static CliException InvalidArgument(string message) => new(ErrorCode.InvalidArgument, message);

    /// <summary>
    /// For a name that matched no item or several; <paramref name="candidates"/> is empty for no match
    /// (spec/cli-surface.md "Errors and exit codes").
    /// </summary>
    public static CliException NameNotUnique(string key, string message, JsonArray candidates) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message)) { Candidates = candidates };

    public static CliException NotImplemented(string message) => new(ErrorCode.NotImplemented, message);

    public static CliException TokenMissing(string message) => new(ErrorCode.TokenMissing, message);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Field(string key, string message) =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [key] = [message] };
}
