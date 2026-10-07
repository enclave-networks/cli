namespace Enclave.Cli.Context;

/// <summary>
/// The partner chosen by one source, before a client is built for it.
/// </summary>
/// <param name="Id">The partner's ID.</param>
/// <param name="Source">What chose it: --partner-id, ENCLAVE_PARTNER_ID, or the path of cli.json.</param>
internal sealed record PartnerChoice(Guid Id, string Source);
