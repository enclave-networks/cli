using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Dns;

/// <summary>
/// The `dns` noun: the organisation's DNS zones and hostnames (proposed-cli-surface.md "Commands").
/// A zone is given by name and a hostname in full (db.internal), or either by --id. dns has no
/// plural.
/// </summary>
internal static class DnsCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("dns", null, "The organisation's DNS zones and hostnames.");

        noun.Add(Show());
        noun.Add(DnsZoneCommands.List());
        noun.Add(DnsZoneCommands.Show());
        noun.Add(DnsZoneCommands.Create());
        noun.Add(DnsZoneCommands.Update());
        noun.Add(DnsZoneCommands.Delete());
        noun.Add(DnsHostnameCommands.List());
        noun.Add(DnsHostnameCommands.Show());
        noun.Add(DnsHostnameCommands.Create());
        noun.Add(DnsHostnameCommands.Update());
        noun.Add(DnsHostnameCommands.Delete());

        return noun;
    }

    private static CliVerb Show()
    {
        var verb = new CliVerb("show", "Show the organisation's DNS summary.", CommandScope.Organisation);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var summary = await org.Client.Dns.GetPropertiesSummaryAsync();

            await context.Output.WriteAsync(summary, context.CancellationToken);
        });

        return verb;
    }
}
