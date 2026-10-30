using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Systems;

internal static class GatewayRoutes
{
    // The subnets given replace the routes a user entered; the routes the system found itself stay.
    // As the portal does (portal-spa detailTab/systems.ts, setSystemDetailsKnownRoutes). A route that
    // stays keeps its weight, the API's preference for it over other gateways' routes to the same
    // subnet. A subnet given that the system also found stays one found route, with the label given:
    // sending it as user-entered too would repeat the subnet, which the portal refuses (portal-spa
    // utils/systems.ts, findSystemSubnetDuplicates).
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

    // The API refuses a route list that holds a subnet twice, comparing the strings exactly (portal
    // SystemPatchModelValidator, MustNotHaveDuplicates on Subnet), so the CLI refuses it before any
    // call.
    public static bool RepeatsASubnet(IReadOnlyList<LabelledValue> given)
    {
        ArgumentNullException.ThrowIfNull(given);

        return given.Select(value => value.Value).Distinct(StringComparer.Ordinal).Count() < given.Count;
    }

    private static bool IsFor(LabelledEntry entry, SystemGatewayRouteModel route) =>
        string.Equals(entry.Value, route.Subnet, StringComparison.Ordinal);
}
