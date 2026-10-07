using Enclave.Cli.Core;
using Enclave.Sdk.Api;

namespace Enclave.Cli.Context;

/// <summary>
/// The token the CLI sends and the API address it sends it to.
/// </summary>
// A class, not a record: a record's generated ToString lists every property, and the token must
// never reach output, diagnostics included.
internal sealed class ApiAccess
{
    public const string TokenVariable = "ENCLAVE_TOKEN";

    public ApiAccess(string token, string tokenSource, Uri baseUrl)
    {
        Token = token;
        TokenSource = tokenSource;
        BaseUrl = baseUrl;
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
    /// The token from ENCLAVE_TOKEN, then credentials.json (proposed-cli-surface.md "Options on
    /// every command"), and the base URL from credentials.json whichever source gave the token.
    /// Exits 3 with token_missing when neither holds a token.
    /// </summary>
    public static ApiAccess Resolve(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = CredentialsFile.PathFor(host);
        var stored = CredentialsFile.Read(host);
        var baseUrl = BaseUrlFrom(host, stored);

        if (host.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } token)
        {
            return new ApiAccess(token, TokenVariable, baseUrl);
        }

        if (stored is { IsValid: false })
        {
            throw CliErrors.TokenMissing($"ENCLAVE_TOKEN is not set and {path} is not valid JSON. Run `enclave-cli login --token-stdin` with a personal access token, or set ENCLAVE_TOKEN.");
        }

        if (stored?.Token is { } savedToken)
        {
            return new ApiAccess(savedToken, path, baseUrl);
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

        if (stored?.BaseUrl is not { } text)
        {
            return host.DefaultApiUrl;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : throw CliErrors.InvalidArgument($"The baseUrl in {CredentialsFile.PathFor(host)} is not an http or https URL.");
    }

    /// <summary>
    /// An Enclave.Sdk.Api client that sends this token to this address. It makes no call.
    /// </summary>
    public EnclaveClient CreateClient() =>
        new(new EnclaveClientOptions { PersonalAccessToken = Token, BaseUrl = BaseUrl.AbsoluteUri });

    public override string ToString() => $"token from {TokenSource}, API at {BaseUrl}";
}
