using Enclave.Cli.Tests.Support;

namespace Enclave.Cli.Tests.Safety;

// A value type, so the public test methods taking one need no null check (CA1062).

// A command that changes local files only: a command line that runs it, and the file whose
// presence afterwards shows it ran.
// Name: The command words, with any option that selects a different source.
// WritesCredentials: Whether the file is credentials.json; otherwise it is cli.json.
// FileExistsAfter: Whether the file exists after the command runs.
public readonly record struct LocalCommand(
    string Name,
    string[] Args,
    bool WritesCredentials,
    bool FileExistsAfter)
{
    // spec/cli-surface.md "Login, logout and status".
    public static IReadOnlyList<LocalCommand> All { get; } =
    [
        new("login", ["login"], true, true),
        new("login --token-stdin", ["login", "--token-stdin"], true, true),
        new("logout", ["logout"], true, false),
        new("org use", ["org", "use", TestData.OrgName], false, true),
        new("org use --id", ["org", "use", "--id", TestData.OrgId.ToString()], false, true),
        new("partner use", ["partner", "use", "--id", TestData.PartnerId.ToString()], false, true),
    ];

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    // Sets up what the command needs to succeed: the organisation lookup, the token on stdin for
    // --token-stdin, and a saved token for logout to delete.
    internal void Arrange(CliRun run)
    {
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        if (Name == "login --token-stdin")
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.StdinText = TestData.Token;
        }

        if (Name == "logout")
        {
            run.SaveCredentials(TestData.Token);
        }
    }

    internal string FilePath(CliRun run) => WritesCredentials ? run.CredentialsPath : run.CliConfigPath;
}
