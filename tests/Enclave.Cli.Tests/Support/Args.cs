namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Builds command-line arguments that many tests share.
/// </summary>
internal static class Args
{
    /// <summary>
    /// --yes when the command needs confirmation, otherwise nothing.
    /// </summary>
    public static string[] YesIf(bool needsYes) => needsYes ? ["--yes"] : [];
}
