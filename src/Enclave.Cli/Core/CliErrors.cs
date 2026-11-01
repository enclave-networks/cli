using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

internal static class CliErrors
{
    // key is an option or argument name, such as "--id".
    public static CliException InvalidArgument(string key, string message) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message));

    // Exit 2 for a problem no single option or argument owns.
    public static CliException InvalidArgument(string message) => new(ErrorCode.InvalidArgument, message);

    // Exit 5 for a name that matched no item, or an organisation the token does not see
    // (spec/cli-surface.md "Errors and exit codes").
    public static CliException NotFound(string key, string message) =>
        new(ErrorCode.NotFound, message, errors: Field(key, message));

    // For a name that matched several items, with them as candidates.
    public static CliException NameNotUnique(string key, string message, JsonArray candidates) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message)) { Candidates = candidates };

    public static CliException NotImplemented(string message) => new(ErrorCode.NotImplemented, message);

    public static CliException TokenMissing(string message) => new(ErrorCode.TokenMissing, message);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Field(string key, string message) =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [key] = [message] };
}
