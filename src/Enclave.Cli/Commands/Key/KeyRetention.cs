using System.CommandLine;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Key;

internal static class KeyRetention
{
    /// <summary>
    /// A duration longer than the API's field can hold exits 2.
    /// </summary>
    // A duration is whole minutes, hours or days (TimeInput.TryParseDuration), so the cast loses
    // nothing. The API refuses more than 30 days with its own message (portal
    // EnrolmentKeyCreateValidator, EnrolmentKeyPatchModelValidator); only a value an int cannot hold
    // is refused here.
    public static int Minutes(TimeSpan duration, Option option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return duration.TotalMinutes <= int.MaxValue
            ? (int)duration.TotalMinutes
            : throw CliErrors.InvalidArgument(option.Name, $"{option.Name} is longer than the API can hold; the API takes up to 30 days.");
    }
}
