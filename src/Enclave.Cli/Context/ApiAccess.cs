using Enclave.Cli.Core;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

// A class, not a record: a record's generated ToString lists every property, and the token must
// never reach output.
internal sealed class ApiAccess
{
    public const string TokenVariable = "ENCLAVE_CLI_PAT";

    // Hidden, as staging is, so no error names it unless it is set (spec/cli-surface.md
    // "Environments").
    public const string EnvironmentVariable = "ENCLAVE_CLI_ENV";

    private const char ByteOrderMark = (char)0xFEFF;

    public ApiAccess(string token, string tokenSource, Uri baseUrl, Uri? partnerApiBaseUrl = null)
    {
        Token = token;
        TokenSource = tokenSource;
        BaseUrl = baseUrl;
        PartnerApiBaseUrl = partnerApiBaseUrl;
    }

    // Never printed.
    public string Token { get; }

    // ENCLAVE_CLI_PAT, the path of credentials.json, or prompt for a token typed at login's question.
    public string TokenSource { get; }

    public Uri BaseUrl { get; }

    // Null leaves Enclave.Sdk.Api's default, production.
    public Uri? PartnerApiBaseUrl { get; }

    public Uri? PartnerApiUrl =>
        PartnerApiBaseUrl ?? (new EnclaveClientOptions().PartnerApiBaseUrl is { } fallback ? new Uri(fallback) : null);

    // The token from ENCLAVE_CLI_PAT, sent to production, or to staging with ENCLAVE_CLI_ENV; otherwise
    // the token in credentials.json, sent to the addresses saved beside it, or to production's where
    // it names none (spec/cli-surface.md "Options on every command"). Exits 3 with token_missing or
    // token_invalid, and 2 for ENCLAVE_CLI_ENV set without ENCLAVE_CLI_PAT or to another value, and
    // for an address in credentials.json that is not an absolute http or https URL.
    public static ApiAccess Resolve(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (TokenFrom(host.GetEnvironmentVariable(TokenVariable), TokenVariable) is { } token)
        {
            return IsStaging(host)
                ? new ApiAccess(token, TokenVariable, host.StagingApiUrl, host.StagingPartnerApiUrl)
                : new ApiAccess(token, TokenVariable, host.DefaultApiUrl, host.DefaultPartnerApiUrl);
        }

        RefuseEnvironmentWithoutToken(host);

        var path = CredentialsFile.PathFor(host);
        var stored = CredentialsFile.Read(host);

        if (path is null)
        {
            throw CliErrors.TokenMissing($"No token: {TokenVariable} is not set, and there is no home directory to hold ~/.enclave/credentials.json, since the user profile path (HOME on Linux and macOS) is empty or not a fully qualified path. Set {TokenVariable}.");
        }

        if (stored is { IsValid: false })
        {
            throw CliErrors.TokenMissing($"{TokenVariable} is not set and {path} is not valid JSON. Run `enclave-cli login` in a terminal, or set {TokenVariable}.");
        }

        var baseUrl = stored?.ApiBaseUrl is { } apiText ? HttpUrl(apiText, path, "apiBaseUrl") : host.DefaultApiUrl;
        var partnerApiBaseUrl = stored?.PartnerApiBaseUrl is { } partnerText ? HttpUrl(partnerText, path, "partnerApiBaseUrl") : host.DefaultPartnerApiUrl;

        return TokenFrom(stored?.Token, path) is { } savedToken
            ? new ApiAccess(savedToken, path, baseUrl, partnerApiBaseUrl)
            : throw CliErrors.TokenMissing($"No token: {TokenVariable} is not set and {path} holds none. Run `enclave-cli login` in a terminal, or set {TokenVariable}.");
    }

    // ENCLAVE_CLI_ENV applies only to ENCLAVE_CLI_PAT; set without it, a saved token would go to its
    // own environment and not the one asked for.
    public static void RefuseEnvironmentWithoutToken(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 })
        {
            throw CliErrors.InvalidArgument(EnvironmentVariable, $"{EnvironmentVariable} applies only to the token in {TokenVariable}, which is not set. Set {TokenVariable}, or unset {EnvironmentVariable}.");
        }
    }

    // Whether ENCLAVE_CLI_PAT holds a token. Left empty, or holding only white space or a byte order
    // mark, it counts as unset, as TokenFrom reads it.
    public static bool TokenVariableIsSet(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return Clean(host.GetEnvironmentVariable(TokenVariable)) is not null;
    }

    // text without a leading byte order mark and surrounding white space; null when nothing is left.
    public static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var token = (text.Length > 0 && text[0] == ByteOrderMark ? text[1..] : text).Trim();

        return token.Length == 0 ? null : token;
    }

    // As Clean, and exits 3 with token_invalid when the token holds anything but printable ASCII
    // other than space; the error names source and holds no part of the token.
    // A token file saved with a UTF-8 byte order mark, as Windows PowerShell 5.1's Out-File
    // -Encoding utf8 writes, gives U+FEFF to a reader that passes the mark on, and to ENCLAVE_CLI_PAT
    // set with $(cat file); Trim leaves it (char.IsWhiteSpace). A token is printable ASCII with no
    // space (RFC 6750 section 2.1, RFC 9110 section 5.5). Another character fails in .NET with no
    // HTTP status, reported as transient or api_error (dotnet/runtime release/10.0,
    // HttpConnection.WriteString, HttpHeaders.CheckContainsNewLineOrNull), or goes into the header
    // as it is. Exiting 3 before any call tells an agent that retrying cannot help.
    public static string? TokenFrom(string? text, string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Clean(text) is not { } token)
        {
            return null;
        }

        return token.All(character => character is >= '!' and <= '~')
            ? token
            : throw new CliException(ErrorCode.TokenInvalid, $"The token from {source} holds a space, a control character or a character outside ASCII, which a personal access token does not hold. Give the token as the portal issued it; personal access tokens are created on the account page of the Enclave portal.");
    }

    // Makes no call, and never disposes handler.
    public EnclaveClient CreateClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var options = new EnclaveClientOptions
        {
            PersonalAccessToken = Token,
            BaseUrl = BaseUrl.AbsoluteUri,
            HttpMessageHandler = handler,
        };

        if (PartnerApiBaseUrl is { } partnerApiBaseUrl)
        {
            options.PartnerApiBaseUrl = partnerApiBaseUrl.AbsoluteUri;
        }

        return new EnclaveClient(options);
    }

    public override string ToString() => $"token from {TokenSource}, API at {BaseUrl}";

    // False for production. Any value but production or staging, ignoring case, exits 2 with an
    // error that lists neither, since staging is hidden.
    private static bool IsStaging(CliHost host)
    {
        var text = host.GetEnvironmentVariable(EnvironmentVariable);

        if (string.IsNullOrEmpty(text) || string.Equals(text, "production", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(text, "staging", StringComparison.OrdinalIgnoreCase)
            ? true
            : throw CliErrors.InvalidArgument(EnvironmentVariable, $"{EnvironmentVariable} is set to a value the CLI does not take. Unset it, or correct it.");
    }

    // Checked here so the error names the file. Enclave.Sdk.Api builds the partner HttpClient with
    // every EnclaveClient (EnclaveClient.SetupPartnerHttpClient), so a partnerApiBaseUrl that is not
    // a URL would fail every command with a UriFormatException.
    private static Uri HttpUrl(string text, string path, string field) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : throw CliErrors.InvalidArgument($"The {field} in {path} is not an absolute http or https URL.");
}
