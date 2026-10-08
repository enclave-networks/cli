using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Trust;

/// <summary>
/// The `trust` noun: trust requirements (proposed-cli-surface.md "Commands"). A trust requirement
/// is given by its description, or by --id.
/// </summary>
internal static class TrustCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("trust", "trusts", "Trust requirements: a sign-in, or a public IP check, that systems must meet.");

        noun.Add(TrustListCommand.Create());
        noun.Add(TrustShowCommand.Create());
        noun.Add(TrustCreateCommand.Create());
        noun.Add(TrustUpdateCommand.Create());
        noun.Add(TrustDeleteCommand.Create());

        return noun;
    }
}
