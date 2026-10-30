using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

/// <summary>
/// With no home directory no file is read, and a command that saves or deletes one exits 2.
/// </summary>
internal static class EnclaveDirectory
{
    /// <summary>
    /// Null when <paramref name="profile"/> is empty or not fully qualified.
    /// </summary>
    // An empty or relative path resolves against the current directory, so the CLI would read a
    // credentials.json from wherever it is run and send the token to that file's baseUrl. .NET gives
    // HOME, and USERPROFILE on Windows when SHGetKnownFolderPath fails, as it is set (dotnet/runtime
    // release/10.0, PersistedFiles.Unix.cs GetHomeDirectory, Environment.Win32.cs GetFolderPathCore).
    public static string? HomeFrom(string profile) =>
        !string.IsNullOrEmpty(profile) && Path.IsPathFullyQualified(profile) ? profile : null;

    /// <summary>
    /// Null when there is no home directory.
    /// </summary>
    public static string? PathOf(CliHost host, string name)
    {
        ArgumentNullException.ThrowIfNull(host);

        return HomeFrom(host.HomeDirectory) is { } home ? Path.Combine(home, ".enclave", name) : null;
    }

    // Runs with the verb's argument checks, before the token is read and before any call, so login
    // does not check a token it cannot save and org use does not look up an organisation it cannot
    // save.
    public static void RequireHome(CliVerb verb)
    {
        ArgumentNullException.ThrowIfNull(verb);

        verb.Check(context =>
        {
            if (HomeFrom(context.Host.HomeDirectory) is null)
            {
                throw NoHome();
            }
        });
    }

    public static CliException NoHome() =>
        CliErrors.InvalidArgument("There is no home directory to save in or delete from: the user profile path (HOME on Linux and macOS) is empty or not a fully qualified path, so there is no ~/.enclave. ENCLAVE_TOKEN, ENCLAVE_ORG or ENCLAVE_ORG_ID, and ENCLAVE_PARTNER_ID work without one.");
}
