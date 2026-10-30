using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Policy;

internal static class PolicyShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a policy.", CommandScope.Organisation);
        var description = verb.Add(CliArguments.OptionalName("policy", "The policy's description, matched whole and ignoring case.", "a policy's description"));
        var id = verb.Add(CliOptions.Id("--id", "The policy's ID.", IdFormats.Int32));

        verb.ExactlyOne(description, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var policyId = await PolicyLookup.IdAsync(context, org, context.Get(id), context.Get(description), description.Name);

            var policy = await SingleItem.CallAsync(() => org.Client.Policies.GetAsync(PolicyId.FromInt(policyId)));
            await context.Output.WriteAsync(policy, context.CancellationToken);
        });

        return verb;
    }
}
