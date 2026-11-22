using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TravelBooking.API.Contracts;
using TravelBooking.Application.Handlers;

namespace TravelBooking.API.Endpoints;

public static class FlightEndpoints
{
    public static IEndpointRouteBuilder MapFlightEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/flights").WithTags("Flights");

        group.MapGet("/", async (
            string from,
            string to,
            DateOnly date,
            SearchFlightsHandler handler,
            CancellationToken ct) =>
        {
            var results = await handler.HandleAsync(from, to, date, ct);
            return Results.Ok(results);
        });

        group.MapGet("/{flightId:guid}", async (
            Guid flightId,
            GetFlightDetailsHandler handler,
            CancellationToken ct) =>
        {
            var details = await handler.HandleAsync(flightId, ct);
            return details is null
                ? Results.NotFound(new ErrorResponse("flight_not_found", $"Flight {flightId} not found."))
                : Results.Ok(details);
        });

        return app;
    }
}
