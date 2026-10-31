using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

// With no home directory, or while ENCLAVE_CLI_PAT is set, no file is read, and a command that saves
// or deletes one exits 2.
internal static class EnclaveDirectory
{
    // Null when profile is empty or not fully qualified.
    // An empty or relative path resolves against the current directory, so the CLI would read a
    // credentials.json from wherever it is run and send the token to that file's apiBaseUrl. .NET
    // gives HOME, and USERPROFILE on Windows when SHGetKnownFolderPath fails, as it is set
    // (dotnet/runtime release/10.0, PersistedFiles.Unix.cs GetHomeDirectory, Environment.Win32.cs
    // GetFolderPathCore).
    public static string? HomeFrom(string profile) =>
        !string.IsNullOrEmpty(profile) && Path.IsPathFullyQualified(profile) ? profile : null;

    // Null when there is no home directory, and while ENCLAVE_CLI_PAT is set, so a CI job or agent
    // session depends only on its own variables and options (spec/cli-surface.md "Options on every
    // command").
    public static string? PathOf(CliHost host, string name)
    {
        ArgumentNullException.ThrowIfNull(host);

        return !ApiAccess.TokenVariableIsSet(host) && HomeFrom(host.HomeDirectory) is { } home
            ? Path.Combine(home, ".enclave", name)
            : null;
    }

    // Runs with the verb's argument checks, before the token is read and before any call, so login
    // does not ask for a token it cannot save and org use does not look up an organisation it cannot
    // save.
    public static void RequireHome(CliVerb verb)
    {
        ArgumentNullException.ThrowIfNull(verb);

        verb.Check(context =>
        {
            if (ApiAccess.TokenVariableIsSet(context.Host))
            {
                throw CliErrors.InvalidArgument(ApiAccess.TokenVariable, $"{ApiAccess.TokenVariable} is set, and while it is, the CLI neither reads nor writes ~/.enclave, where this command saves or deletes. Unset {ApiAccess.TokenVariable} to run it.");
            }

            if (HomeFrom(context.Host.HomeDirectory) is null)
            {
                throw NoHome();
            }
        });
    }

    public static CliException NoHome() =>
        CliErrors.InvalidArgument($"There is no home directory to save in or delete from: the user profile path (HOME on Linux and macOS) is empty or not a fully qualified path, so there is no ~/.enclave. {ApiAccess.TokenVariable}, {OrganisationResolver.NameVariable} or {OrganisationResolver.IdVariable}, and {PartnerResolver.IdVariable} work without one.");
}
