using System.ComponentModel;
using ModelContextProtocol.Server;
using TravelBooking.Application.Handlers;

namespace TravelBooking.MCP.Tools;

[McpServerToolType]
public class FlightTools(
    SearchFlightsHandler searchFlights,
    GetFlightDetailsHandler getFlightDetails)
{
    [McpServerTool(Name = "SearchFlights")]
    [Description("Searches for direct flights between two airports on a specific date. " +
        "Returns a list of flights with id, flight code, airline, departure/arrival times (UTC), " +
        "price in USD, and the number of available seats. " +
        "Use the returned flight id with GetFlightDetails or HoldSeat.")]
    public async Task<IReadOnlyList<FlightSummary>> SearchFlightsAsync(
        [Description("IATA code of the departure airport, e.g. 'AMS'.")] string from,
        [Description("IATA code of the arrival airport, e.g. 'LHR'.")] string to,
        [Description("Travel date in YYYY-MM-DD format.")] DateOnly date,
        CancellationToken cancellationToken)
    {
        return await searchFlights.HandleAsync(from, to, date, cancellationToken);
    }

    [McpServerTool(Name = "GetFlightDetails")]
    [Description("Gets full details for a single flight by its id, including the list of " +
        "available seat numbers. Use this to pick a seat before calling HoldSeat.")]
    public async Task<FlightDetails?> GetFlightDetailsAsync(
        [Description("The flight id returned by SearchFlights.")] Guid flightId,
        CancellationToken cancellationToken)
    {
        return await getFlightDetails.HandleAsync(flightId, cancellationToken);
    }
}