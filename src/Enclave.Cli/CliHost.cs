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

    public static CliHost FromProcess() => new()
    {
        GetEnvironmentVariable = Environment.GetEnvironmentVariable,
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Stdin = Console.In,
        StdinIsTerminal = !Console.IsInputRedirected,
        DefaultApiUrl = new Uri("https://api.enclave.io"),
    };
}
