using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

// Partners are given by ID only: looking them up needs the ReadPartnerList scope, which personal
// access tokens cannot carry (spec/cli-surface.md "Partner API").
internal static class PartnerResolver
{
    public const string IdVariable = "ENCLAVE_PARTNER_ID";

    /// <summary>
    /// Null when no source chooses one. A lower source is read only when no higher one gives a
    /// value. An empty variable counts as unset, and a malformed source that is read exits 2.
    /// </summary>
    public static PartnerChoice? Choose(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Verb?.PartnerIdOption is { } option && context.Get(option) is { } id)
        {
            return new PartnerChoice(id, option.Name);
        }

        var host = context.Host;

        if (host.GetEnvironmentVariable(IdVariable) is { Length: > 0 } text)
        {
            return IdFormats.Guid.TryParse(text, out var guid)
                ? new PartnerChoice(guid, IdVariable)
                : throw CliErrors.InvalidArgument(IdVariable, $"{IdVariable} takes a partner ID: {IdFormats.Guid.Describe}.");
        }

        return SettingsFile.ReadDefault(host, "partner", "partner", "enclave-cli partner use --id <partnerId>") is { } saved
            ? new PartnerChoice(saved.Id, saved.Path)
            : null;
    }

    /// <summary>
    /// Exits 2 with no_partner when none is chosen.
    /// </summary>
    public static PartnerChoice Resolve(CliContext context) =>
        Choose(context)
        ?? throw new CliException(
            ErrorCode.NoPartner,
            $"No partner is chosen. Give --partner-id <partnerId>, set {IdVariable}, or save a default with `enclave-cli partner use --id <partnerId>`. The partner portal shows the partner's ID.");
}
