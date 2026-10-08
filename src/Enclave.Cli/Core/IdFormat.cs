namespace Enclave.Cli.Core;

/// <summary>
/// The form an ID of one kind must have before the CLI puts it in a request.
/// </summary>
/// <typeparam name="T">The ID read from the text.</typeparam>
internal sealed class IdFormat<T>
{
    private readonly Func<string, (bool Valid, T Value)> _parse;

    public IdFormat(string describe, Func<string, (bool Valid, T Value)> parse)
    {
        Describe = describe;
        _parse = parse;
    }

    /// <summary>
    /// The form in words, for error messages: "letters and digits".
    /// </summary>
    public string Describe { get; }

    public bool TryParse(string text, out T value)
    {
        ArgumentNullException.ThrowIfNull(text);

        (var valid, value) = _parse(text);
        return valid;
    }
}
