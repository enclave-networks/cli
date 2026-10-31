using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Options on every command". Cleaning lets a token file saved with a byte
// order mark, read with $(cat file), or a secret pasted with a newline or spaces work. A bad character is
// token_invalid: retrying cannot help, and an error reported as transient would have an agent
// retry it.
public class TokenCleaningTests
{
    private const string CleanToken = "clean-token-3e9a41";

    private const string FileToken = "file-token-71c4d0";

    // Every four characters hold a letter outside a-f, so no run of them can come from a GUID, a
    // port or an English word in an error message by chance.
    private const string Head = "Qx7ZkPw4";

    private const string Tail = "Vm9RtYh3";

    private const char ByteOrderMark = (char)0xFEFF;

    private const string OrgLookup = "/account/orgs";

    private const string Question = "login's question";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string[] Sources = ["ENCLAVE_CLI_PAT", "credentials.json", Question];

    // login's question drops control characters as they are typed or pasted (Spectre.Console 0.57.2,
    // AnsiConsoleExtensions.ReadLine), so they never reach the CLI from there.
    private static readonly string[] ControlCharacters = ["tab", "newline", "delete"];

    // login saves the cleaned token too.
    [TestCaseSource(nameof(DecoratedTokens))]
    public async Task A_token_is_sent_without_a_leading_byte_order_mark_or_surrounding_white_space(string source, string given)
    {
        using var run = CliRun.Start();
        var command = Arrange(run, source, given);

        var request = await CliAssert.AcceptedAsync(run, "GET", command.Path, command.Args);

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {CleanToken}"));

        if (command.Saves)
        {
            Assert.That(JsonAssert.Property(SavedCredentials(run), "personalAccessToken").GetString(), Is.EqualTo(CleanToken));
        }
    }

    // RFC 6750 section 2.1 gives a bearer token as letters, digits and -._~+/=, and header values
    // are visible ASCII (RFC 9110 section 5.5). The error names the source and prints no part of
    // the token, which does not expire (portal TokensApiController.Create). login saves nothing.
    [TestCaseSource(nameof(BadTokens))]
    public async Task A_token_holding_a_character_outside_printable_ASCII_exits_3_with_token_invalid_and_sends_nothing(string source, string character)
    {
        using var run = CliRun.Start();
        var command = Arrange(run, source, Head + character + Tail);

        var result = await run.RunAsync(command.Args);

        CliAssert.Rejected(run, result, "token_invalid");
        var output = result.Stdout + result.Stderr;
        Assert.Multiple(() =>
        {
            Assert.That(Pieces(Head).Concat(Pieces(Tail)).Where(piece => output.Contains(piece, StringComparison.Ordinal)), Is.Empty, "Part of the token is in the output.");
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain(SourceName(run, source)));

            if (command.Saves)
            {
                Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
            }
        });
    }

    // The file's token is sent, as for an empty ENCLAVE_CLI_PAT ("Options on every command").
    [TestCase(" ")]
    [TestCase("\n")]
    [TestCase("﻿")]
    public async Task An_ENCLAVE_CLI_PAT_of_only_white_space_or_a_byte_order_mark_counts_as_unset(string value)
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_CLI_PAT"] = value;
        run.SaveCredentials(FileToken);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", OrgSystems, "system", "list");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {FileToken}"));
    }

    // An answer that holds no token is no answer, and nothing is saved for an empty one.
    [TestCase(" ")]
    [TestCase("﻿")]
    [TestCase("﻿  ")]
    public async Task Login_with_an_answer_of_only_white_space_or_a_byte_order_mark_exits_3_with_token_missing(string answer)
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(answer);
        run.Stub("GET", OrgLookup, json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
    }

    [TestCase(" ")]
    [TestCase("﻿\n")]
    public async Task A_credentials_json_token_of_only_white_space_or_a_byte_order_mark_is_no_token(string value)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");
        run.SaveCredentials(value);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    private static IEnumerable<TestCaseData> DecoratedTokens()
    {
        (string Name, string Given)[] decorations =
        [
            ("byte order mark", ByteOrderMark + CleanToken),
            ("trailing newline", CleanToken + "\n"),
            ("trailing CRLF", CleanToken + "\r\n"),
            ("surrounding spaces", "  " + CleanToken + "  "),
        ];

        foreach (var source in Sources)
        {
            foreach (var (name, given) in decorations)
            {
                yield return new TestCaseData(source, given).SetArgDisplayNames(source, name);
            }
        }
    }

    private static IEnumerable<TestCaseData> BadTokens()
    {
        (string Name, string Character)[] characters =
        [
            ("e acute", "é"),
            ("no-break space", " "),
            ("zero-width space", "​"),
            ("byte order mark", "﻿"),
            ("space", " "),
            ("tab", "\t"),
            ("newline", "\n"),
            ("delete", "\u007F"),
        ];

        foreach (var source in Sources)
        {
            foreach (var (name, character) in characters.Where(pair => source != Question || !ControlCharacters.Contains(pair.Name)))
            {
                yield return new TestCaseData(source, character).SetArgDisplayNames(source, name);
            }
        }
    }

    // system list for the two sources every command reads, and login for the one only it reads.
    private static Command Arrange(CliRun run, string source, string token)
    {
        switch (source)
        {
            case "ENCLAVE_CLI_PAT":
                run.Environment["ENCLAVE_CLI_PAT"] = token;
                run.Stub("GET", OrgSystems, json: ApiJson.Page());
                return new Command(["system", "list"], OrgSystems, Saves: false);
            case "credentials.json":
                run.Environment.Remove("ENCLAVE_CLI_PAT");
                run.SaveCredentials(token);
                run.Stub("GET", OrgSystems, json: ApiJson.Page());
                return new Command(["system", "list"], OrgSystems, Saves: false);
            case Question:
                run.AtTerminal();
                run.AnswerLogin(token);
                run.Stub("GET", OrgLookup, json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
                return new Command(["login"], OrgLookup, Saves: true);
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "No arrangement for this source.");
        }
    }

    private static string SourceName(CliRun run, string source) => source switch
    {
        "credentials.json" => run.CredentialsPath,
        Question => "prompt",
        _ => "ENCLAVE_CLI_PAT",
    };

    private static IEnumerable<string> Pieces(string text) =>
        Enumerable.Range(0, text.Length - 3).Select(start => text.Substring(start, 4));

    private static JsonElement SavedCredentials(CliRun run) =>
        JsonRead.Parse(run.Files.ReadText(run.CredentialsPath) ?? throw new AssertionException("login wrote no credentials.json."));

    private sealed record Command(string[] Args, string Path, bool Saves);
}
