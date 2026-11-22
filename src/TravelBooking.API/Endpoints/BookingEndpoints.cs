using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TravelBooking.API.Contracts;
using TravelBooking.Application;
using TravelBooking.Application.Handlers;

namespace TravelBooking.API.Endpoints;

public static class BookingEndpoints
{
    private const string CanBookPolicy = "CanBook";

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bookings").WithTags("Bookings");

        group.MapPost("/hold", async (
            HoldSeatRequest request,
            ClaimsPrincipal user,
            HoldSeatHandler handler,
            CancellationToken ct) =>
        {
            var hold = await handler.HandleAsync(request.FlightId, request.SeatNumber, user.GetUserId(), ct);
            return Results.Ok(hold);
        });

        group.MapPost("/confirm", async (
            ConfirmBookingRequest request,
            ClaimsPrincipal user,
            ConfirmBookingHandler handler,
            CancellationToken ct) =>
        {
            var booking = await handler.HandleAsync(request.HoldId, user.GetUserId(), ct);
            return Results.Created($"/bookings/{booking.BookingId}", booking);
        });

        group.MapGet("/{bookingId:guid}", async (
            Guid bookingId,
            GetBookingHandler handler,
            CancellationToken ct) =>
        {
            var booking = await handler.HandleAsync(bookingId, ct);
            return booking is null
                ? Results.NotFound(new ErrorResponse("booking_not_found", $"Booking {bookingId} not found."))
                : Results.Ok(booking);
        });

        group.MapDelete("/{bookingId:guid}", async (
            Guid bookingId,
            ClaimsPrincipal user,
            CancelBookingHandler handler,
            CancellationToken ct) =>
        {
            var booking = await handler.HandleAsync(bookingId, user.GetUserId(), ct);
            return Results.Ok(booking);
        });

        return app;
    }
}
