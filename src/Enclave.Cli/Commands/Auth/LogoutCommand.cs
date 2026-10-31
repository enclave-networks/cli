using System.Text.Json.Nodes;
using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Auth;

// spec/cli-surface.md "Login, logout and status".
internal static class LogoutCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "logout",
            "Deletes ~/.enclave/credentials.json.");

        EnclaveDirectory.RequireHome(verb);

        verb.SetHandler(async context =>
        {
            var path = CredentialsFile.PathFor(context.Host) ?? throw EnclaveDirectory.NoHome();
            var deleted = context.Host.Files.Delete(path);

            await context.Output.WriteAsync(new JsonObject { ["path"] = path, ["deleted"] = deleted }, context.CancellationToken);
        });

        return verb;
    }
}
