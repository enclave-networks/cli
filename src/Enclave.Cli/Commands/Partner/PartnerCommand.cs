using Enclave.Cli.Commands.Partner.Customer;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Partner;

internal static class PartnerCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("partner", "partners", "The default partner and the partner's customers.");

        noun.Add(PartnerUseCommand.Create());
        noun.Add(PartnerCustomerCommand.Create());

        return noun;
    }
}
