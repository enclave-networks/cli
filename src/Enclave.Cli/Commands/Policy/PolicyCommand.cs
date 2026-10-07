using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// The `policy` noun (proposed-cli-surface.md "Commands"). A policy is given by its description, or
/// by --id.
/// </summary>
internal static class PolicyCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("policy", "policies", "Policies, which let tagged systems reach each other.");

        noun.Add(PolicyListCommand.Create());
        noun.Add(PolicyShowCommand.Create());
        noun.Add(PolicyCreateCommand.Create());
        noun.Add(PolicyUpdateCommand.Create());
        noun.Add(PolicySeveralCommand.Enable());
        noun.Add(PolicySeveralCommand.Disable());
        noun.Add(PolicySeveralCommand.Delete());

        return noun;
    }
}
