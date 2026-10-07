using Enclave.Cli.Commands.Auth;
using Enclave.Cli.Commands.Describe;
using Enclave.Cli.Commands.Dns;
using Enclave.Cli.Commands.Key;
using Enclave.Cli.Commands.Log;
using Enclave.Cli.Commands.Org;
using Enclave.Cli.Commands.Partner;
using Enclave.Cli.Commands.Policy;
using Enclave.Cli.Commands.Systems;
using Enclave.Cli.Commands.Tag;
using Enclave.Cli.Commands.Trust;
using Enclave.Cli.Core;

namespace Enclave.Cli;

internal static class Program
{
    public static Task<int> Main(string[] args) => CreateRootCommand().Parse(args).InvokeAsync();

    internal static CliRootCommand CreateRootCommand() => CreateRootCommand(CliHost.FromProcess());

    // Each noun builds its own command in its own folder under Commands/, so work on one noun does
    // not touch another's files or this one.
    internal static CliRootCommand CreateRootCommand(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var root = new CliRootCommand(host);

        root.Add(LoginCommand.Create());
        root.Add(LogoutCommand.Create());
        root.Add(StatusCommand.Create());
        root.Add(OrgCommand.Create());
        root.Add(PartnerCommand.Create());
        root.Add(SystemCommand.Create());
        root.Add(KeyCommand.Create());
        root.Add(PolicyCommand.Create());
        root.Add(TagCommand.Create());
        root.Add(DnsCommand.Create());
        root.Add(TrustCommand.Create());
        root.Add(LogCommand.Create());
        root.Add(CommandsCommand.Create());

        return root;
    }
}
