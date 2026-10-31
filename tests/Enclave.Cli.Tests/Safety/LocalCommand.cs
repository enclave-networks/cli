using Enclave.Cli.Tests.Support;

namespace Enclave.Cli.Tests.Safety;

// A value type, so the public test methods taking one need no null check (CA1062).

// A command that changes only ~/.enclave/credentials.json, and a command line that runs it.
// Name: The command words, with any option that selects a different source.
public readonly record struct LocalCommand(string Name, string[] Args)
{
    // spec/cli-surface.md "Login, logout and status".
    public static IReadOnlyList<LocalCommand> All { get; } =
    [
        new("login", ["login"]),
        new("logout", ["logout"]),
        new("org use", ["org", "use", TestData.OrgName]),
        new("org use --id", ["org", "use", "--id", TestData.OrgId.ToString()]),
    ];

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    // Sets up what the command needs to succeed, with ENCLAVE_CLI_PAT unset: the organisation
    // lookup, a person at a terminal answering login, and a saved login for logout to delete and
    // org use to read its token from.
    internal void Arrange(CliRun run)
    {
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        if (Name == "login")
        {
            run.AtTerminal();
            run.AnswerLogin(TestData.Token);
        }
        else
        {
            run.SaveLogin();
        }
    }

    // credentials.json as it stands, or null when there is none; a change shows the command ran.
    internal static string? Saved(CliRun run) => run.Files.ReadText(run.CredentialsPath);
}
