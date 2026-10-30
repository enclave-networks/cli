using System.Text.Json.Nodes;
using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Partner;

internal static class PartnerUseCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "use",
            "Save the default partner in ~/.enclave/cli.json; partner commands use it. Partners are given by ID, which the partner portal shows, since a personal access token cannot look them up. Makes no call, and prints { id }.");

        var id = verb.Add(CliOptions.Id("--id", "The partner's ID.", IdFormats.Guid, "partnerId"));
        verb.ExactlyOne(id);
        EnclaveDirectory.RequireHome(verb);

        // The ID is saved with no call, and so no token, since listing partners needs the
        // ReadPartnerList scope, which personal access tokens cannot carry (spec/cli-surface.md
        // "Partner API").
        verb.SetHandler(async context =>
        {
            var partnerId = context.Get(id)!.Value;

            context.Trace.Saved(SettingsFile.SavePartner(context.Host, partnerId));

            await context.Output.WriteAsync(new JsonObject { ["id"] = partnerId.ToString("D") }, context.CancellationToken);
        });

        return verb;
    }
}
