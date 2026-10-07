namespace Enclave.Cli.Core;

/// <summary>
/// One of the fixed error codes the CLI reports on stderr, with the exit code it exits with.
/// </summary>
internal sealed record ErrorCode(string Code, int ExitCode, string Title)
{
    // proposed-cli-surface.md "Errors and exit codes" fixes the set and each code's exit code;
    // `commands` lists them, and agents act on the code, so a new code is a breaking change.
    public static ErrorCode ApiError { get; } = new("api_error", 1, "API error");

    public static ErrorCode NotImplemented { get; } = new("not_implemented", 1, "Not implemented");

    public static ErrorCode InvalidArgument { get; } = new("invalid_argument", 2, "Invalid argument");

    public static ErrorCode NoOrg { get; } = new("no_org", 2, "No organisation chosen");

    public static ErrorCode NoPartner { get; } = new("no_partner", 2, "No partner chosen");

    public static ErrorCode TokenMissing { get; } = new("token_missing", 3, "No token");

    public static ErrorCode TokenInvalid { get; } = new("token_invalid", 3, "Token refused");

    public static ErrorCode Forbidden { get; } = new("forbidden", 4, "Forbidden");

    public static ErrorCode NotFound { get; } = new("not_found", 5, "Not found");

    public static ErrorCode Transient { get; } = new("transient", 6, "Transient failure");

    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        ApiError,
        NotImplemented,
        InvalidArgument,
        NoOrg,
        NoPartner,
        TokenMissing,
        TokenInvalid,
        Forbidden,
        NotFound,
        Transient,
    ];
}
