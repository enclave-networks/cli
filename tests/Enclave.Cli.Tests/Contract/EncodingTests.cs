using System.Text;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// The CLI reads stdin and writes stdout in one encoding, UTF-8, so a list one run prints and the
// next reads with "-" holds the same text on every OS, non-ASCII names included, and a token file
// saved with a byte order mark gives the token (proposed-cli-surface.md "Several IDs", "Login,
// logout and status"). Console's own encodings differ by OS and by setting: the console code pages
// on Windows (dotnet/runtime release/10.0, src/libraries/System.Console/src/System/
// ConsolePal.Windows.cs, InputEncoding and OutputEncoding, from GetConsoleCP and
// GetConsoleOutputCP), and the charset LANG names on Linux and macOS (ConsolePal.Unix.cs,
// GetConsoleEncoding). A test cannot set the process's console, so these tests give the reader and
// writer the CLI builds for the process (CliHost.FromProcess) bytes of their own.
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

    // The mark says how the file is encoded and is no part of its text, so the token read is the
    // one saved.
    [TestCaseSource(nameof(EncodingsWithAByteOrderMark))]
    public void Stdin_saved_with_a_byte_order_mark_reads_as_its_text_without_the_mark(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Token + "\n")];
        using var reader = CliHost.StdinReader(new MemoryStream(bytes));

        Assert.That(reader.ReadToEnd(), Is.EqualTo(Token + "\n"));
    }

    // Without a mark stdin is read as UTF-8, the encoding the CLI writes.
    [Test]
    public void Stdin_without_a_byte_order_mark_reads_as_utf8()
    {
        using var reader = CliHost.StdinReader(new MemoryStream(Encoding.UTF8.GetBytes(NonAsciiName)));

        Assert.That(reader.ReadToEnd(), Is.EqualTo(NonAsciiName));
    }

    // JSON exchanged between systems is UTF-8 (RFC 8259, section 8.1), with no byte order mark
    // before it (same section), which jq and the JSON readers agents use read.
    [Test]
    public void Redirected_output_is_written_as_utf8_without_a_byte_order_mark()
    {
        using var stream = new MemoryStream();
        using var writer = CliHost.OutputWriter(stream);

        writer.Write(NonAsciiName);

        Assert.That(stream.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(NonAsciiName)));
    }

    // A list goes from one run to the next as bytes: the first run's writer encodes it and the
    // second run's reader decodes it ("Several IDs"). The keys' descriptions are non-ASCII, so a
    // writer and reader that disagree, or a writer for a single-byte code page, change the text the
    // second run reads. That run then deletes the keys the list holds.
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
