using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// Works out the gateway routes `system update --enable-gateway-for` sends.
/// </summary>
internal static class GatewayRoutes
{
    /// <summary>
    /// The routes to send: the subnets given replace the routes a user entered, and the routes the
    /// system found itself stay.
    /// </summary>
    /// <param name="given">The --enable-gateway-for values, each a subnet with an optional label.</param>
    /// <param name="current">The system's routes, read first.</param>
    // As in the portal, the routes the system found itself (userEntered false) stay, and the routes
    // a user entered are replaced by the subnets given (portal-spa
    // redux/reducers/app/detailTab/systems.ts:44-83). New routes have userEntered true and weight 0.
    // A route that stays keeps its weight, the preference the API gives it over other gateways'
    // routes to the same subnet (SystemGatewayRouteModel.Weight), and its label unless one is given
    // (proposed-cli-surface.md "Command options").
    //
    // A subnet given that the system also found stays one found route, with the label given: sending
    // it as a user-entered route as well would repeat the subnet, which the portal refuses as a
    // duplicate (portal-spa src/utils/systems.ts:25-40, findSystemSubnetDuplicates).
    public static IReadOnlyList<SystemGatewayRouteModel> Replace(IReadOnlyList<LabelledValue> given, IReadOnlyList<SystemGatewayRouteModel>? current)
    {
        ArgumentNullException.ThrowIfNull(given);

        var routes = current ?? [];
        var entries = LabelledEntry.Keep(given, routes.Select(route => new LabelledEntry(route.Subnet, route.Name)));
        var found = routes.Where(route => !route.UserEntered).ToArray();
        var result = new List<SystemGatewayRouteModel>(found.Length + entries.Count);

        foreach (var route in found)
        {
            var entry = entries.FirstOrDefault(candidate => IsFor(candidate, route));

            result.Add(new SystemGatewayRouteModel
            {
                Subnet = route.Subnet,
                UserEntered = false,
                Weight = route.Weight,
                Name = entry is null ? route.Name : entry.Label,
            });
        }

        foreach (var entry in entries.Where(candidate => !found.Any(route => IsFor(candidate, route))))
        {
            var stays = routes.FirstOrDefault(route => route.UserEntered && IsFor(entry, route));

            result.Add(new SystemGatewayRouteModel
            {
                Subnet = entry.Value,
                UserEntered = true,
                Weight = stays?.Weight ?? 0,
                Name = entry.Label,
            });
        }

        return result;
    }

    private static bool IsFor(LabelledEntry entry, SystemGatewayRouteModel route) =>
        string.Equals(entry.Value, route.Subnet, StringComparison.Ordinal);
}
