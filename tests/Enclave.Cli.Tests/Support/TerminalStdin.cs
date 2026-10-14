namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The stdin a run gives the CLI while <see cref="CliRun.StdinIsTerminal"/> is set: every read
/// throws and is recorded, and <see cref="CliRun.RunAsync"/> fails the run once one has been made.
/// </summary>
// Reading a terminal waits for someone to type, and the CLI never waits for input (AGENTS.md "CLI
// contract"), so a command decides from CliHost.StdinIsTerminal alone, before any read. The CLI
// turns every exception a command throws into an error it reports (CliAction), so the exception on
// its own could leave a test that expects an error passing; the record lets RunAsync fail the run
// whatever the CLI did with the exception. Each of TextReader's own reads comes down to Read or
// Peek (dotnet/runtime release/10.0, src/libraries/System.Private.CoreLib/src/System/IO/TextReader.cs),
// so those two are overridden; HarnessTests shows every read failing.
internal sealed class TerminalStdin : TextReader
{
    private int _reads;

    /// <summary>
    /// Whether anything tried to read.
    /// </summary>
    public bool WasRead => Volatile.Read(ref _reads) > 0;

    public override int Peek() => Fail();

    public override int Read() => Fail();

    private int Fail()
    {
        Interlocked.Increment(ref _reads);
        throw new InvalidOperationException("stdin is a terminal, which the CLI never reads.");
    }
}
