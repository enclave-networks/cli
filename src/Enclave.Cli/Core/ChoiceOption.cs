using System.CommandLine;

namespace Enclave.Cli.Core;

/// <summary>
/// An option whose values come from a fixed set; <see cref="CliOptions"/> creates them.
/// </summary>
/// <typeparam name="T">The value the option gives a command, null when it is not given.</typeparam>
internal sealed class ChoiceOption<T> : Option<T>, IChoiceOption
{
    public ChoiceOption(string name, IReadOnlyList<string> allowedValues)
        : base(name)
    {
        AllowedValues = allowedValues;
    }

    public IReadOnlyList<string> AllowedValues { get; }
}
