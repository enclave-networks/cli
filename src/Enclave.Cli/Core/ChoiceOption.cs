using System.CommandLine;

namespace Enclave.Cli.Core;

internal sealed class ChoiceOption<T> : Option<T>, IChoiceOption
{
    public ChoiceOption(string name, IReadOnlyList<string> allowedValues)
        : base(name)
    {
        AllowedValues = allowedValues;
    }

    public IReadOnlyList<string> AllowedValues { get; }
}
