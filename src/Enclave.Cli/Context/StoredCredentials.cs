namespace Enclave.Cli.Context;

/// <summary>
/// The contents of credentials.json. Token and BaseUrl are null when the file leaves them out or
/// empty, and both are null when the file is not a JSON object (IsValid false).
/// </summary>
internal sealed record StoredCredentials(string? Token, string? BaseUrl, bool IsValid);
