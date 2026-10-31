using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

// Partners are given by ID only: looking them up needs the ReadPartnerList scope, which personal
// access tokens cannot carry (spec/cli-surface.md "Partner API").
internal static class PartnerResolver
{
    public const string IdVariable = "ENCLAVE_CLI_PARTNER_ID";

    // Null when neither source chooses one. The variable is read only without the option. An empty
    // variable counts as unset, and a malformed one that is read exits 2.
    public static PartnerChoice? Choose(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Verb?.PartnerIdOption is { } option && context.Get(option) is { } id)
        {
            return new PartnerChoice(id, option.Name);
        }

        if (context.Host.GetEnvironmentVariable(IdVariable) is not { Length: > 0 } text)
        {
            return null;
        }

        return IdFormats.Guid.TryParse(text, out var guid)
            ? new PartnerChoice(guid, IdVariable)
            : throw CliErrors.InvalidArgument(IdVariable, $"{IdVariable} takes a partner ID: {IdFormats.Guid.Describe}.");
    }

    // Exits 2 with no_partner when none is chosen.
    public static PartnerChoice Resolve(CliContext context) =>
        Choose(context)
        ?? throw new CliException(
            ErrorCode.NoPartner,
            $"No partner is chosen. Give --partner-id <partnerId>, or set {IdVariable}. The partner portal shows the partner's ID.");
}
