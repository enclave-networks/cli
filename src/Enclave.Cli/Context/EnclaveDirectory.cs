using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

/// <summary>
/// ~/.enclave, the directory under the user's home directory that holds credentials.json and
/// cli.json. With no home directory there is no ~/.enclave: no file is read, and a command that
/// saves or deletes one exits 2.
/// </summary>
internal static class EnclaveDirectory
{
    /// <summary>
    /// <paramref name="profile"/>, the user profile path, as the home directory; null when it is
    /// empty or not a fully qualified path, which names no home directory.
    /// </summary>
    // An empty or relative path resolves against the current directory, so ~/.enclave would be
    // wherever the CLI is run: it would read a credentials.json found there and send the token to
    // that file's baseUrl. On Linux and macOS the profile path is HOME as it is set, relative or
    // not (dotnet/runtime release/10.0,
    // src/libraries/System.Private.CoreLib/src/System/IO/PersistedFiles.Unix.cs, GetHomeDirectory,
    // lines 53-55). On Windows, when SHGetKnownFolderPath fails it is USERPROFILE as it is set
    // (Environment.Win32.cs, GetFolderPathCore, lines 349-352 and 381-385).
    public static string? HomeFrom(string profile) =>
        !string.IsNullOrEmpty(profile) && Path.IsPathFullyQualified(profile) ? profile : null;

    /// <summary>
    /// The path of <paramref name="name"/> in ~/.enclave, or null when there is no home directory.
    /// </summary>
    public static string? PathOf(CliHost host, string name)
    {
        ArgumentNullException.ThrowIfNull(host);

        return HomeFrom(host.HomeDirectory) is { } home ? Path.Combine(home, ".enclave", name) : null;
    }

    /// <summary>
    /// Refuses the command, with the verb's other argument checks, when there is no home directory
    /// for the file it saves or deletes.
    /// </summary>
    // The check runs before the token is read and before any call, so login does not check a token
    // it cannot save, and org use does not look up an organisation it cannot save.
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

    /// <summary>
    /// Exit 2: there is no home directory to save in or delete from.
    /// </summary>
    public static CliException NoHome() =>
        CliErrors.InvalidArgument("There is no home directory to save in or delete from: the user profile path (HOME on Linux and macOS) is empty or not a fully qualified path, so there is no ~/.enclave. ENCLAVE_TOKEN, ENCLAVE_ORG or ENCLAVE_ORG_ID, and ENCLAVE_PARTNER_ID work without one.");
}
