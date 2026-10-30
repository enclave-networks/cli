namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The stdin a run gives the CLI while <see cref="CliRun.StdinIsTerminal"/> is set: every read
/// throws and is recorded, and <see cref="CliRun.RunAsync"/> then fails the run.
/// </summary>
// The CLI reports every exception a command throws as an error (CliAction), so the exception alone
// could leave a test that expects an error passing; the record fails the run whatever the CLI did
// with it. Every TextReader read comes down to Read or Peek (dotnet/runtime release/10.0,
// TextReader.cs).
internal sealed class TerminalStdin : TextReader
{
    private int _reads;

    public bool WasRead => Volatile.Read(ref _reads) > 0;

    public override int Peek() => Fail();

    public override int Read() => Fail();

    private int Fail()
    {
        Interlocked.Increment(ref _reads);
        throw new InvalidOperationException("stdin is a terminal, which the CLI never reads.");
    }
}
