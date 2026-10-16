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
    /// The user profile path, which can be empty or relative; EnclaveDirectory.HomeFrom decides
    /// whether it names a home directory.
    /// </summary>
    // FromProcess asks .NET for the path without checking that the directory exists
    // (SpecialFolderOption.DoNotVerify). With the default, SpecialFolderOption.None, .NET returns ""
    // on Linux and macOS for a directory that does not exist or cannot be read (dotnet/runtime
    // release/10.0, src/libraries/System.Private.CoreLib/src/System/Environment.GetFolderPathCore.Unix.cs,
    // GetFolderPathCore, lines 24-39), as for a container whose HOME does not exist, and "" would
    // put ~/.enclave in the current directory. The path as given keeps ~/.enclave where HOME says.
    public required string HomeDirectory { get; init; }

    public required TextReader Stdin { get; init; }

    public required bool StdinIsTerminal { get; init; }

    public required Uri DefaultApiUrl { get; init; }

    /// <summary>
    /// The partner API address when credentials.json names none, or null to leave Enclave.Sdk.Api's
    /// own default, production (EnclaveClientOptions.PartnerApiBaseUrl).
    /// </summary>
    public Uri? DefaultPartnerApiUrl { get; init; }

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

    /// <summary>
    /// Creates the handler beneath the CLI's own (ApiRequestHandler) that sends each request to the
    /// network; null gives HttpClientHandler.
    /// </summary>
    // A test seam, which FromProcess leaves null. A fake API on loopback answers every request it
    // gets, so it cannot produce a failure such as HttpClient's timeout, and a run whose partner API
    // address is left at production's needs a network that sends nothing. Each command's
    // ApiRequestHandler disposes the handler this creates.
    internal Func<HttpMessageHandler>? CreateNetworkHandler { get; init; }

    // The CLI reads stdin and writes redirected stdout and stderr as UTF-8 on every OS, so a list one
    // run prints reads back the same in the next, non-ASCII names included, and other programs get
    // JSON in the encoding JSON is exchanged in.
    //
    // Console's own encodings follow the machine. On Windows they are the console code pages
    // (dotnet/runtime release/10.0, src/libraries/System.Console/src/System/ConsolePal.Windows.cs,
    // InputEncoding and OutputEncoding, from GetConsoleCP and GetConsoleOutputCP), read through
    // OSEncoding for a code page such as 437 that .NET does not build in
    // (src/libraries/Common/src/System/Text/EncodingHelper.Windows.cs, GetSupportedConsoleEncoding).
    // On Linux and macOS they are the charset LC_ALL, LC_MESSAGES or LANG names, and UTF-8 when none
    // names one (ConsolePal.Unix.cs, GetConsoleEncoding; EncodingHelper.Unix.cs, GetCharset). A
    // single-byte code page writes a character it lacks as "?". The input and output code pages on
    // Windows can differ, as can the locale of two processes in a pipe, so the run that reads a list
    // can decode it in an encoding other than the one it was written in. Console.In also reads a
    // byte order mark as a character (GetOrCreateReader on each OS,
    // detectEncodingFromByteOrderMarks: false), which a token file saved with one starts with.
    //
    // JSON exchanged between systems is UTF-8 with no byte order mark (RFC 8259, section 8.1), the
    // form jq and agents' JSON readers read. The trade-off: a program that decodes the CLI's output
    // with the console code page shows non-ASCII characters wrongly, as Windows PowerShell 5.1 does
    // through [Console]::OutputEncoding (Microsoft Learn, about_Character_Encoding). That is
    // accepted because agents, the CLI's first users, read JSON as UTF-8, and a PowerShell user can
    // set [Console]::OutputEncoding to UTF-8. A terminal keeps Console's writer, which writes in the
    // encoding the terminal shows text in, so a person reading it sees every character that encoding
    // has; a console at a single-byte code page such as 437 or 850 shows the others as "?".
    //
    // System.CommandLine writes to Console.Out and Console.Error unless the invocation is given
    // other writers (InvocationConfiguration.Output and Error, version 2.0.12, which read them on
    // first use), so the CLI's writers replace them there.
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
    /// Reads stdin as UTF-8, or in the encoding a byte order mark at its start names, without the mark.
    /// </summary>
    internal static TextReader StdinReader(Stream stdin) =>
        new StreamReader(stdin, Utf8, detectEncodingFromByteOrderMarks: true);

    /// <summary>
    /// Writes redirected stdout or stderr as UTF-8 without a byte order mark, each write as it is made.
    /// </summary>
    // Console's own writer flushes each write (Console.CreateOutputWriter, AutoFlush = true), and
    // nothing flushes a writer when the process ends, so this one flushes each write too.
    internal static TextWriter OutputWriter(Stream output) =>
        new StreamWriter(output, Utf8) { AutoFlush = true };
}
