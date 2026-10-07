using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

/// <summary>
/// The errors command code reports most often.
/// </summary>
internal static class CliErrors
{
    /// <summary>
    /// Exit 2: the value of <paramref name="key"/> (an option or argument name, such as "--id") is
    /// wrong. The message is the detail and the one entry under that key in errors.
    /// </summary>
    public static CliException InvalidArgument(string key, string message) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message));

    /// <summary>
    /// Exit 2 for a problem no single option or argument owns.
    /// </summary>
    public static CliException InvalidArgument(string message) => new(ErrorCode.InvalidArgument, message);

    /// <summary>
    /// Exit 2: a name matched no item or several. <paramref name="candidates"/> holds the matches,
    /// and is empty for no match (proposed-cli-surface.md "Errors and exit codes").
    /// </summary>
    public static CliException NameNotUnique(string key, string message, JsonArray candidates) =>
        new(ErrorCode.InvalidArgument, message, errors: Field(key, message)) { Candidates = candidates };

    /// <summary>
    /// Exit 1: the command exists but cannot run until Enclave.Sdk.Api gains what it needs. It
    /// makes no call.
    /// </summary>
    public static CliException NotImplemented(string message) => new(ErrorCode.NotImplemented, message);

    /// <summary>
    /// Exit 3: there is no token to send.
    /// </summary>
    public static CliException TokenMissing(string message) => new(ErrorCode.TokenMissing, message);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Field(string key, string message) =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [key] = [message] };
}
