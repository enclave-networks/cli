using System.CommandLine;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// --keep-disconnected: how long a system enrolled with an ephemeral key is kept after it
/// disconnects, which the API takes in whole minutes (EnrolmentKeyCreateModel and
/// EnrolmentKeyPatchModel, DisconnectedRetentionMinutes).
/// </summary>
internal static class KeyRetention
{
    /// <summary>
    /// The duration in minutes. A duration longer than the API's field can hold exits 2.
    /// </summary>
    // A duration is a whole number of minutes, hours or days (TimeInput.TryParseDuration), so its
    // minutes are whole. The API refuses more than 30 days (portal EnrolmentKeyCreateValidator.cs:26-28,
    // EnrolmentKeyPatchModelValidator.cs:18) and reports that itself with its own message; a number
    // beyond a 32-bit integer cannot be sent at all.
    public static int Minutes(TimeSpan duration, Option option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return duration.TotalMinutes <= int.MaxValue
            ? (int)duration.TotalMinutes
            : throw CliErrors.InvalidArgument(option.Name, $"{option.Name} is longer than the API can hold; the API takes up to 30 days.");
    }
}
