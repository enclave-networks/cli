namespace Enclave.Cli.Context;

// Token, ApiBaseUrl, PartnerApiBaseUrl and DefaultOrgId are null when the file leaves them out or
// empty, and all four are null when IsValid is false. DefaultOrgId holds the value as written,
// unchecked.
// A class, not a record: a record's generated ToString lists every property, and the token must
// never reach output.
internal sealed class StoredCredentials
{
    public StoredCredentials(string? token, string? apiBaseUrl, string? partnerApiBaseUrl, string? defaultOrgId, bool isValid)
    {
        Token = token;
        ApiBaseUrl = apiBaseUrl;
        PartnerApiBaseUrl = partnerApiBaseUrl;
        DefaultOrgId = defaultOrgId;
        IsValid = isValid;
    }

    // Never printed.
    public string? Token { get; }

    public string? ApiBaseUrl { get; }

    public string? PartnerApiBaseUrl { get; }

    public string? DefaultOrgId { get; }

    public bool IsValid { get; }
}
