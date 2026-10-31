using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Trust;

internal static class TrustCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("trust", "trusts", "Trust requirements.");

        noun.Add(TrustListCommand.Create());
        noun.Add(TrustShowCommand.Create());
        noun.Add(TrustCreateCommand.Create());
        noun.Add(TrustUpdateCommand.Create());
        noun.Add(TrustDeleteCommand.Create());

        return noun;
    }
}
