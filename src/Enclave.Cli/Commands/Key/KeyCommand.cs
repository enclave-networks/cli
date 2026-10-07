using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// The `key` noun: enrolment keys (proposed-cli-surface.md "Commands"). A key is given by its
/// description, or by --id.
/// </summary>
internal static class KeyCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("key", "keys", "Enrolment keys, which systems enrol with.");

        noun.Add(KeyListCommand.Create());
        noun.Add(KeyShowCommand.Create());
        noun.Add(KeyCreateCommand.Create());
        noun.Add(KeyUpdateCommand.Create());
        noun.Add(KeyBulkCommands.Enable());
        noun.Add(KeyBulkCommands.Disable());
        noun.Add(KeyBulkCommands.Delete());

        return noun;
    }
}
