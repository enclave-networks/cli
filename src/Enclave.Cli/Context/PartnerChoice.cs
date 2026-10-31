namespace Enclave.Cli.Context;

// The partner as one source chose it, before a client is built for it. Source is --partner-id or
// ENCLAVE_CLI_PARTNER_ID.
internal sealed record PartnerChoice(Guid Id, string Source);
