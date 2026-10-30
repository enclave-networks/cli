using System.Diagnostics.CodeAnalysis;
using System.Text;
using Enclave.Cli.Storage;

namespace Enclave.Cli;

/// <summary>
/// The process environment the CLI reads, so tests can supply their own.
/// </summary>
internal sealed class CliHost
{
    /// <summary>
    /// Production's Enclave API address, which the CLI calls when credentials.json names none.
    /// </summary>
    public const string ApiUrl = "https://api.enclave.io";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public required Func<string, string?> GetEnvironmentVariable { get; init; }

    /// <summary>
    /// Can be empty or relative; EnclaveDirectory.HomeFrom decides whether it names a home directory.
    /// </summary>
    // FromProcess passes SpecialFolderOption.DoNotVerify: with the default, .NET returns "" on Linux
    // and macOS when the directory does not exist or cannot be read, as in a container whose HOME
    // does not exist, which would put ~/.enclave in the current directory (dotnet/runtime
    // release/10.0, Environment.GetFolderPathCore.Unix.cs, GetFolderPathCore).
    public required string HomeDirectory { get; init; }

    public required TextReader Stdin { get; init; }

    public required bool StdinIsTerminal { get; init; }

    public required Uri DefaultApiUrl { get; init; }

    /// <summary>
    /// Used when credentials.json names none; null leaves Enclave.Sdk.Api's default, production
    /// (EnclaveClientOptions.PartnerApiBaseUrl).
    /// </summary>
    public Uri? DefaultPartnerApiUrl { get; init; }

    /// <summary>
    /// Every file the CLI reads or writes goes through this store.
    /// </summary>
    public required IFileStore Files { get; init; }

    /// <summary>
    /// The CLI reads the current time and the local time zone (<see cref="TimeProvider.LocalTimeZone"/>)
    /// only through this, so a test can fix both.
    /// </summary>
    public required TimeProvider Time { get; init; }

    /// <summary>
    /// Creates the handler beneath ApiRequestHandler that sends each request to the network; null
    /// gives HttpClientHandler. ApiRequestHandler disposes it.
    /// </summary>
    // A test seam: a fake API cannot produce a failure such as HttpClient's timeout, and a run whose
    // partner API address is production's needs a network that sends nothing.
    internal Func<HttpMessageHandler>? CreateNetworkHandler { get; init; }

    // Redirected stdout and stderr are written, and stdin read, as UTF-8 on every OS, so a list one
    // run prints reads back the same in the next. Console's own encodings follow the console code
    // page on Windows and the locale on Linux and macOS, can differ between the two ends of a pipe,
    // and write a character a single-byte code page lacks as "?" (dotnet/runtime release/10.0,
    // ConsolePal.Windows.cs and ConsolePal.Unix.cs). Console.In also reads a byte order mark as a
    // character, which a token file saved with one starts with.
    //
    // Trade-off: a program that decodes the output with the console code page, as Windows PowerShell
    // 5.1 does, shows non-ASCII characters wrongly. Accepted because agents read JSON as UTF-8 (RFC
    // 8259, section 8.1), and a PowerShell user can set [Console]::OutputEncoding to UTF-8. A terminal
    // keeps Console's writer, which writes in the encoding the terminal shows.
    //
    // System.CommandLine 2.0.12 writes to Console.Out and Console.Error unless
    // InvocationConfiguration.Output and Error are set, and reads them on first use, so replacing
    // them here covers its output too.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each writer is Console's for the rest of the process, and writes every write through as it is made.")]
    public static CliHost FromProcess()
    {
        if (Console.IsOutputRedirected)
        {
            Console.SetOut(OutputWriter(Console.OpenStandardOutput()));
        }

        if (Console.IsErrorRedirected)
        {
            Console.SetError(OutputWriter(Console.OpenStandardError()));
        }

        return new()
        {
            GetEnvironmentVariable = Environment.GetEnvironmentVariable,
            HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            Stdin = StdinReader(Console.OpenStandardInput()),
            StdinIsTerminal = !Console.IsInputRedirected,
            DefaultApiUrl = new Uri(ApiUrl),
            Files = new DiskFileStore(),
            Time = TimeProvider.System,
        };
    }

    /// <summary>
    /// UTF-8, or the encoding a byte order mark at the start names; the mark is not returned.
    /// </summary>
    // Console.In passes a byte order mark through on Linux and macOS (dotnet/runtime release/10.0,
    // ConsolePal.Unix.cs GetOrCreateReader, detectEncodingFromByteOrderMarks: false), so stdin is read
    // through this reader on every OS ("CLI contract" in AGENTS.md).
    internal static TextReader StdinReader(Stream stdin) =>
        new StreamReader(stdin, Utf8, detectEncodingFromByteOrderMarks: true);

    // Nothing flushes a writer when the process ends, so each write is flushed, as Console's own
    // writer does (Console.CreateOutputWriter, AutoFlush = true).
    internal static TextWriter OutputWriter(Stream output) =>
        new StreamWriter(output, Utf8) { AutoFlush = true };
}
