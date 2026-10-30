namespace Enclave.Cli.Context;

// The partner as one source chose it, before a client is built for it. Source is --partner-id,
// ENCLAVE_PARTNER_ID, or the path of cli.json.
internal sealed record PartnerChoice(Guid Id, string Source);
