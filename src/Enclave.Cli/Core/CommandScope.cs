namespace Enclave.Cli.Core;

// What a command acts within, which decides the context options it takes.
[Flags]
internal enum CommandScope
{
    // Neither an organisation nor a partner: login, logout, org list.
    None = 0,

    // The organisation in use: the command takes --org and --org-id.
    Organisation = 1,

    // The partner in use: the command takes --partner-id.
    Partner = 2,
}
