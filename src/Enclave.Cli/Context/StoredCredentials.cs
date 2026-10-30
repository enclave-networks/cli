namespace Enclave.Cli.Context;

// Token, BaseUrl and PartnerApiBaseUrl are null when the file leaves them out or empty, and all
// three are null when IsValid is false.
// A class, not a record: a record's generated ToString lists every property, and the token must
// never reach output.
internal sealed class StoredCredentials
{
    public StoredCredentials(string? token, string? baseUrl, string? partnerApiBaseUrl, bool isValid)
    {
        Token = token;
        BaseUrl = baseUrl;
        PartnerApiBaseUrl = partnerApiBaseUrl;
        IsValid = isValid;
    }

    // Never printed.
    public string? Token { get; }

    public string? BaseUrl { get; }

    public string? PartnerApiBaseUrl { get; }

    public bool IsValid { get; }
}
