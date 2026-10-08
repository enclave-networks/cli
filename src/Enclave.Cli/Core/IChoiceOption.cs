namespace Enclave.Cli.Core;

/// <summary>
/// An option that takes one of a fixed set of values, which help and `commands` list.
/// </summary>
internal interface IChoiceOption
{
    IReadOnlyList<string> AllowedValues { get; }
}
