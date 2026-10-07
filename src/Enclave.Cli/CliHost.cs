using Enclave.Cli.Storage;

namespace Enclave.Cli;

/// <summary>
/// The process environment the CLI reads, so tests can supply their own.
/// </summary>
internal sealed class CliHost
{
    public required Func<string, string?> GetEnvironmentVariable { get; init; }

    public required string HomeDirectory { get; init; }

    public required TextReader Stdin { get; init; }

    public required bool StdinIsTerminal { get; init; }

    public required Uri DefaultApiUrl { get; init; }

    /// <summary>
    /// Every file the CLI reads or writes goes through this store.
    /// </summary>
    public required IFileStore Files { get; init; }

    /// <summary>
    /// The clock, and the system's time zone as <see cref="TimeProvider.LocalTimeZone"/>. The CLI reads the current time
    /// and the local time zone only through this.
    /// </summary>
    // Durations and the check that an --until time has not passed count from now, and a time
    // without a zone is read in the system's time zone (proposed-cli-surface.md "Command options",
    // "Details"), so a test fixes both here to get one answer on any machine.
    public required TimeProvider Time { get; init; }

    public static CliHost FromProcess() => new()
    {
        GetEnvironmentVariable = Environment.GetEnvironmentVariable,
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Stdin = Console.In,
        StdinIsTerminal = !Console.IsInputRedirected,
        DefaultApiUrl = new Uri("https://api.enclave.io"),
        Files = new DiskFileStore(),
        Time = TimeProvider.System,
    };
}
