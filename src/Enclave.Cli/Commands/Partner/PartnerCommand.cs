using Enclave.Cli.Commands.Partner.Customer;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Partner;

internal static class PartnerCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("partner", "partners", "The partner and its customers.");

        noun.Add(PartnerCustomerCommand.Create());

        return noun;
    }
}
