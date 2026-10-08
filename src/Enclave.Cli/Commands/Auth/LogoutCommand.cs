using System.Text.Json.Nodes;
using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Auth;

/// <summary>
/// `logout`: deletes the saved token file (proposed-cli-surface.md "Login, logout and status").
/// </summary>
internal static class LogoutCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "logout",
            "Delete ~/.enclave/credentials.json. The token stays valid until it is revoked in the portal, and other tools built on Enclave.Sdk.Api, which read the same file, lose it too. Prints { path, deleted }.");

        // logout needs no token, makes no call, and leaves cli.json, which holds settings and says
        // nothing about who is signed in.
        verb.SetHandler(async context =>
        {
            var path = CredentialsFile.PathFor(context.Host);
            var deleted = context.Host.Files.Delete(path);

            await context.Output.WriteAsync(new JsonObject { ["path"] = path, ["deleted"] = deleted }, context.CancellationToken);
        });

        return verb;
    }
}
