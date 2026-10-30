using System.Text;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// stdin and stdout are UTF-8 on every OS, so a list one run prints and the next reads with "-"
// keeps non-ASCII names, and a token file saved with a byte order mark gives the token
// (spec/cli-surface.md "Several IDs", "Login, logout and status"). Console's own encodings follow
// the code page on Windows and LANG on Linux and macOS (dotnet/runtime release/10.0,
// ConsolePal.Windows.cs InputEncoding and OutputEncoding, ConsolePal.Unix.cs GetConsoleEncoding).
// A test cannot set the process's console, so these tests give the reader and writer that
// CliHost.FromProcess builds bytes of their own.
public class EncodingTests
{
    private const string Token = "stdin-token-3f0d2a";

    // Letters outside every single-byte code page between them, so an encoder for any one code page
    // writes at least one of them as "?".
    private const string NonAsciiName = "Zürich Łódź 東京";

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // Windows PowerShell 5.1 saves UTF-8 with a byte order mark with Out-File -Encoding utf8, and
    // UTF-16 LE with a mark with Out-File's default encoding and the > operator (Microsoft Learn,
    // about_Character_Encoding, PowerShell 5.1).
    public static IEnumerable<TestCaseData> EncodingsWithAByteOrderMark()
    {
        yield return new TestCaseData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)).SetArgDisplayNames("UTF-8");
        yield return new TestCaseData(new UnicodeEncoding(bigEndian: false, byteOrderMark: true)).SetArgDisplayNames("UTF-16 LE");
    }

    [TestCaseSource(nameof(EncodingsWithAByteOrderMark))]
    public void Stdin_saved_with_a_byte_order_mark_reads_as_its_text_without_the_mark(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Token + "\n")];
        using var reader = CliHost.StdinReader(new MemoryStream(bytes));

        Assert.That(reader.ReadToEnd(), Is.EqualTo(Token + "\n"));
    }

    // UTF-8 is the encoding the CLI writes.
    [Test]
    public void Stdin_without_a_byte_order_mark_reads_as_utf8()
    {
        using var reader = CliHost.StdinReader(new MemoryStream(Encoding.UTF8.GetBytes(NonAsciiName)));

        Assert.That(reader.ReadToEnd(), Is.EqualTo(NonAsciiName));
    }

    // JSON exchanged between systems is UTF-8 with no byte order mark (RFC 8259, section 8.1).
    [Test]
    public void Redirected_output_is_written_as_utf8_without_a_byte_order_mark()
    {
        using var stream = new MemoryStream();
        using var writer = CliHost.OutputWriter(stream);

        writer.Write(NonAsciiName);

        Assert.That(stream.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(NonAsciiName)));
    }

    // The list passes between runs as bytes through the CLI's own writer and reader, so a writer
    // and reader that disagree, or a single-byte code page, change the names the second run reads.
    [Test]
    public async Task A_list_printed_and_piped_into_another_run_reads_back_unchanged_with_non_ascii_names()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(21, NonAsciiName), ApiJson.Key(22, "東京 build agents")));
        run.StubBulk("DELETE", KeysPath, "keysDeleted", 2);
        var listed = await run.RunAsync("key", "list");
        CliAssert.List(listed, "key");

        using var pipe = new MemoryStream();
        using var writer = CliHost.OutputWriter(pipe);
        await writer.WriteAsync(listed.Stdout);
        using var reader = CliHost.StdinReader(new MemoryStream(pipe.ToArray()));
        run.StdinText = await reader.ReadToEndAsync();
        var deleted = await run.RunAsync("key", "delete", "-");

        Assert.That(run.StdinText, Is.EqualTo(listed.Stdout));
        CliAssert.Bulk(deleted, 2, 2);
        Assert.That(JsonRead.IntList(JsonAssert.Property(run.RequestsTo("DELETE", KeysPath).Single().BodyJson, "keyIds")), Is.EqualTo("21,22"));
    }
}
