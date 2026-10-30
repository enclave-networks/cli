using Enclave.Cli.Core;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

// A class, not a record: a record's generated ToString lists every property, and the token must
// never reach output, diagnostics included.
internal sealed class ApiAccess
{
    public const string TokenVariable = "ENCLAVE_TOKEN";

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

    // ENCLAVE_TOKEN, the path of credentials.json, or stdin (login --token-stdin).
    public string TokenSource { get; }

    // credentials.json's baseUrl, or the default API address.
    public Uri BaseUrl { get; }

    // credentials.json's partnerApiBaseUrl, or the host's default partner API address; null leaves
    // Enclave.Sdk.Api's default, production.
    public Uri? PartnerApiBaseUrl { get; }

    // The token from ENCLAVE_TOKEN, then credentials.json (spec/cli-surface.md "Options on every
    // command"), with the base URLs from credentials.json whichever source gave the token. Exits 3
    // with token_missing or token_invalid, and 2 when a base URL in the file is not http or https.
    public static ApiAccess Resolve(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = CredentialsFile.PathFor(host);
        var stored = CredentialsFile.Read(host);
        var baseUrl = BaseUrlFrom(host, stored);
        var partnerApiBaseUrl = PartnerApiBaseUrlFrom(host, stored);

        if (TokenFrom(host.GetEnvironmentVariable(TokenVariable), TokenVariable) is { } token)
        {
            return new ApiAccess(token, TokenVariable, baseUrl, partnerApiBaseUrl);
        }

        if (path is null)
        {
            throw CliErrors.TokenMissing("No token: ENCLAVE_TOKEN is not set, and there is no home directory to hold ~/.enclave/credentials.json, since the user profile path (HOME on Linux and macOS) is empty or not a fully qualified path. Set ENCLAVE_TOKEN.");
        }

        if (stored is { IsValid: false })
        {
            throw CliErrors.TokenMissing($"ENCLAVE_TOKEN is not set and {path} is not valid JSON. Run `enclave-cli login --token-stdin` with a personal access token, or set ENCLAVE_TOKEN.");
        }

        if (TokenFrom(stored?.Token, path) is { } savedToken)
        {
            return new ApiAccess(savedToken, path, baseUrl, partnerApiBaseUrl);
        }

        throw CliErrors.TokenMissing($"No token: ENCLAVE_TOKEN is not set and {path} holds none. Run `enclave-cli login --token-stdin` with a personal access token, or set ENCLAVE_TOKEN.");
    }

    // Null when nothing is left once a leading byte order mark and surrounding white space are
    // dropped. Exits 3 with token_invalid when the token holds anything but printable ASCII other
    // than space; the error names source and holds no part of the token.
    // A token file saved with a UTF-8 byte order mark, as Windows PowerShell 5.1's Out-File
    // -Encoding utf8 writes, gives U+FEFF to a reader that passes the mark on, and to ENCLAVE_TOKEN
    // set with $(cat file); Trim leaves it (char.IsWhiteSpace). A token is printable ASCII with no
    // space (RFC 6750 section 2.1, RFC 9110 section 5.5). Another character fails in .NET with no
    // HTTP status, reported as transient or api_error (dotnet/runtime release/10.0,
    // HttpConnection.WriteString, HttpHeaders.CheckContainsNewLineOrNull), or goes into the header
    // as it is. Exiting 3 before any call tells an agent that retrying cannot help.
    public static string? TokenFrom(string? text, string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (text is null)
        {
            return null;
        }

        var token = (text.Length > 0 && text[0] == ByteOrderMark ? text[1..] : text).Trim();

        if (token.Length == 0)
        {
            return null;
        }

        return token.All(character => character is >= '!' and <= '~')
            ? token
            : throw new CliException(ErrorCode.TokenInvalid, $"The token from {source} holds a space, a control character or a character outside ASCII, which a personal access token does not hold. Give the token as the portal issued it; personal access tokens are created on the account page of the Enclave portal.");
    }

    // credentials.json's baseUrl whichever source gives the token, so a file pointing at another
    // API address keeps pointing there; the default API address when the file has none.
    public static Uri BaseUrlFrom(CliHost host, StoredCredentials? stored)
    {
        ArgumentNullException.ThrowIfNull(host);

        return stored?.BaseUrl is { } text ? HttpUrl(host, text, "baseUrl") : host.DefaultApiUrl;
    }

    // credentials.json's partnerApiBaseUrl whichever source gives the token, as baseUrl is
    // (spec/cli-surface.md "Partner API"); the host's default when the file has none, which is null
    // outside tests.
    // Checked here so the error names the file and the setting. Enclave.Sdk.Api builds the partner
    // HttpClient with every EnclaveClient (EnclaveClient.SetupPartnerHttpClient), so a value that is
    // not a URL would fail every command with a UriFormatException.
    public static Uri? PartnerApiBaseUrlFrom(CliHost host, StoredCredentials? stored)
    {
        ArgumentNullException.ThrowIfNull(host);

        return stored?.PartnerApiBaseUrl is { } text ? HttpUrl(host, text, "partnerApiBaseUrl") : host.DefaultPartnerApiUrl;
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

    private static Uri HttpUrl(CliHost host, string text, string setting) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : throw CliErrors.InvalidArgument($"The {setting} in {CredentialsFile.PathFor(host)} is not an http or https URL.");
}
