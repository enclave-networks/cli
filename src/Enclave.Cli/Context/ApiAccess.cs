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
    /// The token from ENCLAVE_TOKEN, then credentials.json (proposed-cli-surface.md "Options on
    /// every command"), and the base URLs from credentials.json whichever source gave the token.
    /// Exits 3 with token_missing when neither holds a token, and 2 when a base URL in the file is
    /// not an http or https URL.
    /// </summary>
    public static ApiAccess Resolve(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = CredentialsFile.PathFor(host);
        var stored = CredentialsFile.Read(host);
        var baseUrl = BaseUrlFrom(host, stored);
        var partnerApiBaseUrl = PartnerApiBaseUrlFrom(host, stored);

        if (host.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } token)
        {
            return new ApiAccess(token, TokenVariable, baseUrl, partnerApiBaseUrl);
        }

        if (stored is { IsValid: false })
        {
            throw CliErrors.TokenMissing($"ENCLAVE_TOKEN is not set and {path} is not valid JSON. Run `enclave-cli login --token-stdin` with a personal access token, or set ENCLAVE_TOKEN.");
        }

        if (stored?.Token is { } savedToken)
        {
            return new ApiAccess(savedToken, path, baseUrl, partnerApiBaseUrl);
        }

        throw CliErrors.TokenMissing($"No token: ENCLAVE_TOKEN is not set and {path} holds none. Run `enclave-cli login --token-stdin` with a personal access token, or set ENCLAVE_TOKEN.");
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
    /// (proposed-cli-surface.md "Partner API"); the host's default partner API address when the file
    /// has none, which is null outside tests.
    /// </summary>
    // Enclave.Sdk.Api builds the partner API's HttpClient with the EnclaveClient, from
    // new Uri(PartnerApiBaseUrl) (EnclaveClient.SetupPartnerHttpClient, Enclave.Sdk.Api 1.1.0), so a
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
