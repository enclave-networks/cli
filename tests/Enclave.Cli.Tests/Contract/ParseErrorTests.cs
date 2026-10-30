using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// System.CommandLine's default parse-error action writes text and help and exits 1
// (ParseErrorAction, System.CommandLine 2.0.12); the CLI replaces it with one JSON error and exit 2
// (spec/cli-surface.md "Errors and exit codes"). CliAssert.Rejected also checks that stdout is
// empty and no request was made.
public class ParseErrorTests
{
    private const string Secret = "s3cret-value";

    // In the form of an Enclave personal access token, 63 characters of Base58 (portal
    // AccessTokenStore.StoreReferenceTokenAsync). It starts with lower-case letters, so a name read
    // up to the first character that cannot be in an option's name would take some of it.
    private const string GluedToken = "kqN4tR8vXw2LmZ9pHc6Jd3Fs7Gb5Ya1Ue8Wr4Tn2Qx6Vk9Mh3Pj7Cz5Ef1Ad8Bg";

    // The errors key and the message of an unknown option written in a form no option of the CLI
    // takes, which is reported without its name.
    private const string UnnamedKey = "options";

    private const string UnnamedOnSystemList = "`system list` was given an option it does not have, which is not repeated here because it is not written as an option name: \"--\" and lower-case words joined by hyphens. Run `enclave-cli commands system list` for the options it takes.";

    private static readonly string[] BogusAndWrong = ["--bogus", "--wrong"];

    public static IEnumerable<TestCaseData> MalformedCommands()
    {
        yield return CommandCases.Described("an unknown option", "system", "list", "--bogus");
        yield return CommandCases.Described("an unknown noun", "widget", "list");
        yield return CommandCases.Described("an unknown verb", "system", "frobnicate");
        yield return CommandCases.Described("an unexpected argument", "system", "list", "extra");
        yield return CommandCases.Described("a noun without a verb", "system");
        yield return CommandCases.Described("a missing argument", "system", "show");
        yield return CommandCases.Described("a missing argument after a hyphenated verb", "dns", "create-zone");
        yield return CommandCases.Described("an option without its value", "system", "list", "--filter");
        yield return CommandCases.Described("a number option given a word", "key", "create", "ci runners", "--uses", "many");
    }

    // The corrected command shows the rejection came from the old form and not from a command that
    // does not run at all ("Commands").
    public static IEnumerable<TestCaseData> ReplacedCommands()
    {
        yield return CommandCases.RejectedThenCorrected("dns zone list", "dns list-zones", "GET", TestData.OrgPath("dns/zones"), ApiJson.Page(ApiJson.Zone(4, "internal")));
        yield return CommandCases.RejectedThenCorrected("dns record list", "dns list-hostnames", "GET", TestData.OrgPath("dns/records"), ApiJson.Page(ApiJson.Record(7, "db")));
        yield return CommandCases.RejectedThenCorrected("org user list", "org list-users", "GET", TestData.OrgPath("users"), ApiJson.Users((TestData.OtherOrgId, "sam@acme.example")));
        yield return CommandCases.RejectedThenCorrected("pending list", "system list --pending", "GET", TestData.OrgPath("unapproved-systems"), ApiJson.Page(ApiJson.PendingSystem("ABCDE")));
        yield return CommandCases.RejectedThenCorrected("log list", "log", "GET", TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("System ABCDE enrolled")));
    }

    // AGENTS.md "CLI contract" and "Command options" leave these out. An option a command does not
    // take, such as --partner-id on an organisation command, is unknown to it ("Details").
    public static IEnumerable<TestCaseData> OptionsTheCliDoesNotHave()
    {
        var systems = TestData.OrgPath("systems");
        var systemsPage = ApiJson.Page(ApiJson.System("ABCDE"));

        yield return CommandCases.RejectedThenCorrected("system list -o json", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list -o table", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --output json", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system show ABCDE -o json", "system show ABCDE", "GET", TestData.OrgPath("systems/ABCDE"), ApiJson.System("ABCDE"));
        yield return CommandCases.RejectedThenCorrected("system list --all", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --limit 5", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --search web", "system list --filter web", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --token not-a-token", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected($"system list --partner-id {TestData.PartnerId}", "system list", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("policy update --id 42 --from-file policy.json", "policy update --id 42 --notes Reviewed", "PATCH", TestData.OrgPath("policies/42"), ApiJson.Policy(42));
        yield return CommandCases.RejectedThenCorrected("policy update --id 42 --template", "policy update --id 42 --notes Reviewed", "PATCH", TestData.OrgPath("policies/42"), ApiJson.Policy(42));
    }

    // A value given to an unknown option could be a token passed by mistake, so no error or
    // --verbose diagnostic repeats any part of it ("Errors and exit codes"). A token glued to a name,
    // or a name with a capital, leaves the option unnamed. Each case gives the value, the names
    // errors is keyed by in order (null for an unnamed option), and the suggestion detail carries.
    public static IEnumerable<TestCaseData> UnknownOptionsWithValues()
    {
        yield return Unknown("--token value", Secret, ["--token"], null, "--token", Secret);
        yield return Unknown("--token=value", Secret, ["--token"], null, $"--token={Secret}");
        yield return Unknown("--token:value", Secret, ["--token"], null, $"--token:{Secret}");
        yield return Unknown("\"--token value\" in one argument", Secret, ["--token"], null, $"--token {Secret}");
        yield return Unknown("-t value", Secret, ["-t"], null, "-t", Secret);
        yield return Unknown("-tvalue", Secret, ["-t"], null, $"-t{Secret}");
        yield return Unknown("--filtr:value", Secret, ["--filtr"], "--filter", $"--filtr:{Secret}");
        yield return Unknown("a token glued to --token", GluedToken, [null], null, $"--token{GluedToken}");
        yield return Unknown("--Token=value", Secret, [null], null, $"--Token={Secret}");
        yield return Unknown("--token value --verbose", Secret, ["--token"], null, "--token", Secret, "--verbose");
        yield return Unknown("--api-key value", Secret, ["--api-key"], null, "--api-key", Secret);
        yield return Unknown("--password value --bogus", Secret, ["--password", "--bogus"], null, "--password", Secret, "--bogus");
    }

    public static IEnumerable<TestCaseData> WordsAfterUnknownOptions()
    {
        yield return After("--bogus=x extra", ["--bogus", "arguments"], "--bogus=x", "extra");
        yield return After("--bogus:x extra", ["--bogus", "arguments"], "--bogus:x", "extra");
        yield return After("\"--bogus x\" extra", ["--bogus", "arguments"], "--bogus x", "extra");
        yield return After("-bx extra", ["-b", "arguments"], "-bx", "extra");
        yield return After("--Bogus=x extra", [UnnamedKey, "arguments"], "--Bogus=x", "extra");
        yield return After("--bogus x", ["--bogus"], "--bogus", "x");
        yield return After("-b x", ["-b"], "-b", "x");
    }

    // The fake API records a request whether or not a stub answers it, so a CLI that sends one
    // despite the parse error shows.
    [TestCaseSource(nameof(MalformedCommands))]
    public async Task A_parse_error_exits_2_with_one_invalid_argument_error_naming_the_problem(string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Is.Not.Null.And.Not.Empty, result.ToString());
    }

    // errors has the API's shape, so an agent reads it the same way whether the API or the CLI wrote
    // it ("Errors and exit codes"). An object's keys have no order, so they are compared as a set.
    [Test]
    public async Task A_command_line_with_several_problems_gives_one_error_whose_detail_names_the_first_and_whose_errors_lists_them_all()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("system", "list", "--bogus", "--wrong");

        CliAssert.Rejected(run, result);
        var error = result.Error;
        var errors = error.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("detail").GetString(), Does.Contain("--bogus").And.Not.Contain("--wrong"));
            Assert.That(errors.ValueKind, Is.EqualTo(JsonValueKind.Object), errors.ToString());
        });
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(errors), Is.EquivalentTo(BogusAndWrong), errors.ToString());

            foreach (var option in errors.EnumerateObject())
            {
                Assert.That(JsonAssert.Strings(option.Value), Is.Not.Empty.And.All.Not.Empty, errors.ToString());
            }
        });
    }

    // detail and each message are compared whole, so a name that took any of the value, or a
    // suggestion worked out from the value, shows; the value itself must appear nowhere on stderr.
    [TestCaseSource(nameof(UnknownOptionsWithValues))]
    public async Task An_error_never_repeats_the_value_given_to_an_unknown_option(string value, string?[] names, string? suggestion, string[] args)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(names);
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        var error = result.Error;
        var errors = error.GetProperty("errors");
        var keys = names.Select(name => name ?? UnnamedKey).ToArray();
        var first = names[0] is { } name ? NoOptionOnSystemList(name, suggestion) : UnnamedOnSystemList;
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Does.Not.Contain(value));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo(first));
            Assert.That(JsonRead.PropertyNames(errors), Is.EquivalentTo(keys), errors.ToString());
            Assert.That(JsonAssert.Strings(errors.GetProperty(keys[0])), Is.EqualTo(new[] { first }), errors.ToString());
        });
    }

    [TestCaseSource(nameof(WordsAfterUnknownOptions))]
    public async Task A_word_after_an_unknown_option_is_its_value_only_when_the_option_carries_none_of_its_own(string[] keys, string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(["system", "list", .. args]);

        CliAssert.Rejected(run, result);
        var errors = result.Error.GetProperty("errors");
        Assert.That(JsonRead.PropertyNames(errors), Is.EquivalentTo(keys), errors.ToString());
    }

    // An unknown option is named only when written in this form ("Errors and exit codes"), so a
    // mistyped option is named only while every option the CLI has is in it. Every command knows
    // help's -?, /h and /? (HelpOption, System.CommandLine 2.0.12), so they are never unknown.
    [Test]
    public void Every_option_and_alias_is_written_in_the_form_an_unknown_option_is_named_in()
    {
        using var run = CliRun.Start();
        var root = Program.CreateRootCommand(run.CreateHost());
        var names = new List<string>();
        var commands = new Stack<System.CommandLine.Command>([root]);

        while (commands.TryPop(out var command))
        {
            foreach (var option in command.Options)
            {
                names.Add(option.Name);
                names.AddRange(option.Aliases);
            }

            foreach (var subcommand in command.Subcommands)
            {
                commands.Push(subcommand);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("--filter").And.Contain("-h"));
            Assert.That(names.Where(name => !IsOptionForm(name)).Distinct(), Is.Empty);
        });
    }

    [TestCaseSource(nameof(ReplacedCommands))]
    public async Task A_command_line_the_command_tree_does_not_have_is_a_parse_error(string[] rejected, string[] corrected, string method, string path, string json)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: json);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, corrected, method, path);
    }

    [TestCaseSource(nameof(OptionsTheCliDoesNotHave))]
    public async Task An_option_the_command_does_not_take_is_a_parse_error(string[] rejected, string[] corrected, string method, string path, string json)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: json);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, corrected, method, path);
    }

    [Test]
    public async Task A_parse_error_names_the_unknown_option_in_detail()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("system", "list", "--bogus");

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain("--bogus"));
    }

    // An agent corrects a mistyped name from the suggestion without calling `commands`. The
    // suggestions are System.CommandLine's typo corrections (System.CommandLine 2.0.12).
    [TestCase("system lst", "list")]
    [TestCase("sytem", "system")]
    [TestCase("system list --filtr", "--filter")]
    [TestCase("dns list-zone", "list-zones")]
    public async Task A_mistyped_name_carries_the_suggestion_in_detail(string command, string suggestion)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();

        var result = await run.RunAsync(command.Split(' '));

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain(suggestion));
    }

    // Arguments are checked before the token and the organisation ("Errors and exit codes").
    [TestCase("system lst")]
    [TestCase("system list --bogus")]
    [TestCase("system list --bogus --wrong")]
    public async Task A_parse_error_is_the_same_with_or_without_a_token_or_organisation(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var withContext = CliRun.Start();
        using var withoutContext = CliRun.Start();
        withoutContext.Environment.Remove("ENCLAVE_TOKEN");
        withoutContext.Environment.Remove("ENCLAVE_ORG_ID");

        var withResult = await withContext.RunAsync(command.Split(' '));
        var withoutResult = await withoutContext.RunAsync(command.Split(' '));

        CliAssert.Rejected(withContext, withResult);
        CliAssert.Rejected(withoutContext, withoutResult);
        Assert.That(withoutResult.Stderr, Is.EqualTo(withResult.Stderr));
    }

    // System.CommandLine's [suggest] directive prints completions as plain text (SuggestDirective,
    // System.CommandLine 2.0.12), and stdout is always JSON ("Output").
    [TestCase("[suggest]")]
    [TestCase("[suggest:4]")]
    public async Task The_suggest_directive_is_refused_with_invalid_argument_and_prints_nothing_on_stdout(string directive)
    {
        using var run = CliRun.Start();
        var systems = TestData.OrgPath("systems");
        run.Stub("GET", systems, json: ApiJson.Page(ApiJson.System("ABCDE")));

        await CliAssert.RejectedThenAcceptedAsync(run, [directive, "system", "list"], ["system", "list"], "GET", systems);
    }

    // System.CommandLine reads an argument that starts with "@" as a response file
    // (ParserConfiguration.ResponseFileTokenReplacer, on by default in System.CommandLine 2.0.12).
    // "@" can begin an ordinary value, and such a read would go around CliHost.Files. The run's files
    // hold ops at both paths the name could mean, so a lookup through CliHost.Files would send its
    // text; no ops exists on disk, so expanding a response file would exit 2.
    [Test]
    public async Task A_value_that_starts_with_an_at_sign_is_sent_as_given_and_reads_no_file()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies/42");
        run.Stub("PATCH", path, json: ApiJson.Policy(42));
        run.WriteFile("ops", "from the file");
        run.Files.WriteText(Path.GetFullPath("ops"), "from the file", privateToUser: false);

        var request = await CliAssert.AcceptedAsync(run, "PATCH", path, "policy", "update", "--id", "42", "--notes", "@ops");

        Assert.That(JsonAssert.Property(request.BodyJson, "notes").GetString(), Is.EqualTo("@ops"));
    }

    private static TestCaseData Unknown(string description, string value, string?[] names, string? suggestion, params string[] args) =>
        new TestCaseData(value, names, suggestion, (string[])["system", "list", .. args]).SetArgDisplayNames(description);

    private static TestCaseData After(string description, string[] keys, params string[] args) =>
        new TestCaseData(keys, args).SetArgDisplayNames(description);

    private static string NoOptionOnSystemList(string name, string? suggestion) =>
        suggestion is null ? $"`system list` has no option {name}." : $"`system list` has no option {name}. Did you mean {suggestion}?";

    // "--" and lower-case words joined by single hyphens, "-" and one letter or digit, or one of
    // help's -?, /h and /?.
    private static bool IsOptionForm(string name) =>
        name is "-?" or "/h" or "/?"
        || (name.Length == 2 && name[0] == '-' && char.IsAsciiLetterOrDigit(name[1]))
        || (name.StartsWith("--", StringComparison.Ordinal) && name[2..].Split('-').All(word => word.Length > 0 && word.All(char.IsAsciiLetterLower)));
}
