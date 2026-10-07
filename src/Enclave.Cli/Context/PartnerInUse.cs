namespace Enclave.Cli.Context;

/// <summary>
/// The partner a partner command acts for.
/// </summary>
/// <param name="Id">The partner's ID.</param>
/// <param name="Source">What chose it: --partner-id, ENCLAVE_PARTNER_ID, or the path of cli.json.</param>
internal sealed record PartnerInUse(Guid Id, string Source);
