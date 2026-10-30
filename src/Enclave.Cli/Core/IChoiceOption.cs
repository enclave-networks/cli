namespace Enclave.Cli.Core;

// An option that takes one of a fixed set of values, which help and `commands` list.
internal interface IChoiceOption
{
    IReadOnlyList<string> AllowedValues { get; }
}
