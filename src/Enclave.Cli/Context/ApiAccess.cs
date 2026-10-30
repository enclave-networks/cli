using Enclave.Cli.Core;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

/// <summary>
/// The token the CLI sends and the API addresses it sends it to.
/// </summary>
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

    /// <summary>
    /// The personal access token. Never printed.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// ENCLAVE_TOKEN, or the path of credentials.json.
    /// </summary>
    public string TokenSource { get; }

    /// <summary>
    /// credentials.json's baseUrl, or the default API address.
    /// </summary>
    public Uri BaseUrl { get; }

    /// <summary>
    /// credentials.json's partnerApiBaseUrl, or the host's default partner API address; null leaves
    /// Enclave.Sdk.Api's default, production.
    /// </summary>
    public Uri? PartnerApiBaseUrl { get; }

    /// <summary>
    /// The token from ENCLAVE_TOKEN, then credentials.json (spec/cli-surface.md "Options on
    /// every command"), each cleaned by <see cref="TokenFrom"/>, and the base URLs from
    /// credentials.json whichever source gave the token. Exits 3 with token_missing when neither
    /// holds a token, 3 with token_invalid when the token used holds a character no token holds, and
    /// 2 when a base URL in the file is not an http or https URL.
    /// </summary>
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

    /// <summary>
    /// The token <paramref name="text"/> holds, from any source: without a leading byte order mark,
    /// then without surrounding white space. Null when nothing else is left, which counts as no
    /// token. Exits 3 with token_invalid when the token holds any character but printable ASCII
    /// other than space; the error names <paramref name="source"/> and holds no part of the token.
    /// </summary>
    // A token file saved with a UTF-8 byte order mark starts with U+FEFF when its reader passes the
    // mark on (LoginCommand.ReadTokenAsync gives the cases for stdin), and ENCLAVE_TOKEN set from
    // such a file with $(cat file) holds it too. Trim leaves it, since U+FEFF is not white space
    // (char.IsWhiteSpace).
    //
    // A token is printable ASCII with no space: a bearer token is letters, digits and -._~+/=
    // (RFC 6750 section 2.1), and an HTTP header value is visible ASCII (RFC 9110 section 5.5).
    // Sent as it is, another character fails in .NET with no HTTP status: SocketsHttpHandler writes
    // header values as ASCII and throws HttpRequestException for a character outside it
    // (dotnet/runtime release/10.0,
    // src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs,
    // WriteString), which the CLI reports as transient, and a newline throws FormatException
    // (Headers/HttpHeaders.cs, CheckContainsNewLineOrNull), which it reports as api_error. A space,
    // a tab or DEL goes into the header as it is (observed with .NET 10). Checked here, each exits
    // 3 before any call, which tells an agent that retrying cannot help.
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

    /// <summary>
    /// credentials.json's baseUrl, kept whichever source gives the token so that a file pointing
    /// at another API address keeps pointing there; the default API address when the file has none.
    /// </summary>
    public static Uri BaseUrlFrom(CliHost host, StoredCredentials? stored)
    {
        ArgumentNullException.ThrowIfNull(host);

        return stored?.BaseUrl is { } text ? HttpUrl(host, text, "baseUrl") : host.DefaultApiUrl;
    }

    /// <summary>
    /// credentials.json's partnerApiBaseUrl, kept whichever source gives the token as baseUrl is
    /// (spec/cli-surface.md "Partner API"); the host's default partner API address when the file
    /// has none, which is null outside tests.
    /// </summary>
    // Enclave.Sdk.Api builds the partner API's HttpClient with the EnclaveClient, from
    // new Uri(PartnerApiBaseUrl) (EnclaveClient.SetupPartnerHttpClient, Enclave.Sdk.Api), so a
    // value that is not a URL would fail every command with a UriFormatException. It is checked here
    // so the error names the file and the setting.
    public static Uri? PartnerApiBaseUrlFrom(CliHost host, StoredCredentials? stored)
    {
        ArgumentNullException.ThrowIfNull(host);

        return stored?.PartnerApiBaseUrl is { } text ? HttpUrl(host, text, "partnerApiBaseUrl") : host.DefaultPartnerApiUrl;
    }

    /// <summary>
    /// An Enclave.Sdk.Api client that sends this token to these addresses, every request going
    /// through <paramref name="handler"/> (EnclaveClientOptions.HttpMessageHandler). It makes no
    /// call, and never disposes the handler.
    /// </summary>
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
