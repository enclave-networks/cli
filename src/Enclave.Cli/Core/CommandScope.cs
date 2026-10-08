namespace Enclave.Cli.Core;

/// <summary>
/// What a command acts within, which decides the context options it takes.
/// </summary>
[Flags]
internal enum CommandScope
{
    /// <summary>
    /// Neither an organisation nor a partner: login, logout, org list.
    /// </summary>
    None = 0,

    /// <summary>
    /// The organisation in use: the command takes --org and --org-id.
    /// </summary>
    Organisation = 1,

    /// <summary>
    /// The partner in use: the command takes --partner-id.
    /// </summary>
    Partner = 2,
}
