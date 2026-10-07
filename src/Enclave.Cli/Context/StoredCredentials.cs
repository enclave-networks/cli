namespace Enclave.Cli.Context;

/// <summary>
/// The contents of credentials.json. Token, BaseUrl and PartnerApiBaseUrl are null when the file
/// leaves them out or empty, and all three are null when the file is not a JSON object (IsValid
/// false).
/// </summary>
internal sealed record StoredCredentials(string? Token, string? BaseUrl, string? PartnerApiBaseUrl, bool IsValid);
