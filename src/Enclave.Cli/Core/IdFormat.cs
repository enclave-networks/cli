namespace Enclave.Cli.Core;

internal sealed class IdFormat<T>
{
    private readonly Func<string, (bool Valid, T Value)> _parse;

    public IdFormat(string describe, Func<string, (bool Valid, T Value)> parse)
    {
        Describe = describe;
        _parse = parse;
    }

    /// <summary>
    /// For error messages: "letters and digits".
    /// </summary>
    public string Describe { get; }

    public bool TryParse(string text, out T value)
    {
        ArgumentNullException.ThrowIfNull(text);

        (var valid, value) = _parse(text);
        return valid;
    }
}
