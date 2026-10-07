using System.Text.Json.Nodes;
using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

/// <summary>
/// Chooses the partner a partner command acts for (proposed-cli-surface.md "Context"). Partners
/// are given by ID only: looking them up needs the ReadPartnerList scope, which personal access
/// tokens cannot carry ("Partner API").
/// </summary>
internal static class PartnerResolver
{
    public const string IdVariable = "ENCLAVE_PARTNER_ID";

    /// <summary>
    /// The partner --partner-id, ENCLAVE_PARTNER_ID or cli.json chooses, in that order, reading a
    /// lower source only when no higher one gives a value; null when none does. An empty variable
    /// counts as unset, and a malformed source that is read exits 2.
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

        var path = SettingsFile.PathFor(host);

        if (SettingsFile.Read(host)?["partner"] is not JsonObject saved)
        {
            return null;
        }

        return saved["id"] is JsonValue value && value.TryGetValue(out string? savedId) && IdFormats.Guid.TryParse(savedId, out var savedGuid)
            ? new PartnerChoice(savedGuid, path)
            : throw CliErrors.InvalidArgument($"The default partner in {path} has no valid ID. Save it again with `enclave-cli partner use --id <partnerId>`.");
    }

    /// <summary>
    /// The partner for a partner command; exits 2 with no_partner, naming both ways to choose one,
    /// when none is chosen.
    /// </summary>
    public static PartnerChoice Resolve(CliContext context) =>
        Choose(context)
        ?? throw new CliException(
            ErrorCode.NoPartner,
            $"No partner is chosen. Give --partner-id <partnerId>, set {IdVariable}, or save a default with `enclave-cli partner use --id <partnerId>`. The partner portal shows the partner's ID.");
}
