using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TravelBooking.Application.Handlers;
using TravelBooking.Domain;

namespace TravelBooking.MCP.Tools;

[McpServerToolType]
public class BookingTools(
    HoldSeatHandler holdSeat,
    ConfirmBookingHandler confirmBooking,
    GetBookingHandler getBooking,
    CancelBookingHandler cancelBooking,
    ILogger<BookingTools> logger)
{
    [McpServerTool(Name = "HoldSeat")]
    [Description("Places a temporary hold on a specific seat of a flight. The hold expires " +
        "after a few minutes; confirm it with ConfirmBooking before it expires. " +
        "Returns the hold id and the exact UTC expiration time.")]
    public async Task<HoldResult> HoldSeatAsync(
        [Description("The flight id returned by SearchFlights.")] Guid flightId,
        [Description("The seat number to hold, e.g. '12A'. Must be one of the available " +
            "seats returned by GetFlightDetails.")] string seatNumber,
        CancellationToken cancellationToken)
    {
        return await holdSeat.HandleAsync(flightId, seatNumber, userId: null, cancellationToken);
    }

    [McpServerTool(Name = "ConfirmBooking")]
    [Description("Confirms a seat hold and turns it into a booking. Fails if the hold has " +
        "already expired. Returns the booking with its booking reference.")]
    public async Task<BookingResult> ConfirmBookingAsync(
        [Description("The hold id returned by HoldSeat.")] Guid holdId,
        CancellationToken cancellationToken)
    {
        try
        {
            var booking = await confirmBooking.HandleAsync(holdId, userId: null, cancellationToken);
            logger.LogInformation("Booking {BookingReference} confirmed for hold {HoldId}",
                booking.BookingReference, holdId);
            return booking;
        }
        catch (BookingDomainException ex)
        {
            logger.LogWarning(ex, "ConfirmBooking failed for hold {HoldId}: {Reason}",
                holdId, ex.Message);
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "GetBooking")]
    [Description("Gets a booking by its id, including its status (Confirmed or Cancelled), " +
        "seat number and booking reference.")]
    public async Task<BookingResult?> GetBookingAsync(
        [Description("The booking id returned by ConfirmBooking.")] Guid bookingId,
        CancellationToken cancellationToken)
    {
        return await getBooking.HandleAsync(bookingId, cancellationToken);
    }

    [McpServerTool(Name = "CancelBooking")]
    [Description("Cancels an existing booking, freeing up its seat. Returns the cancelled " +
        "booking. Fails if the booking is already cancelled.")]
    public async Task<BookingResult> CancelBookingAsync(
        [Description("The booking id returned by ConfirmBooking.")] Guid bookingId,
        CancellationToken cancellationToken)
    {
        return await cancelBooking.HandleAsync(bookingId, userId: null, cancellationToken);
    }
}