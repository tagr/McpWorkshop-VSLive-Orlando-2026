using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using TravelBooking.Domain;
using TravelBooking.Domain.Models;
using TravelBooking.Domain.Services;
using TravelBooking.Infrastructure.Data;

namespace TravelBooking.Infrastructure.Services;

public class BookingService(
    TravelBookingDbContext db,
    TimeProvider clock,
    IOptions<BookingOptions> options) : IBookingService
{
    private const int BookingReferenceRetryAttempts = 5;
    private const string PostgresUniqueViolation = "23505";

    public async Task<SeatHold> HoldSeatAsync(
        Guid flightId,
        string seatNumber,
        string? userId,
        CancellationToken cancellationToken)
    {
        var flight = await db.Flights.FirstOrDefaultAsync(f => f.Id == flightId, cancellationToken)
            ?? throw new BookingDomainException($"Flight {flightId} not found.");

        var now = clock.GetUtcNow().UtcDateTime;

        var holds = await db.SeatHolds
            .Where(h => h.FlightId == flightId && h.Status == HoldStatus.Active)
            .ToListAsync(cancellationToken);
        var bookings = await db.Bookings
            .Where(b => b.FlightId == flightId && b.Status == BookingStatus.Confirmed)
            .ToListAsync(cancellationToken);
        var occupancy = new FlightOccupancy(holds, bookings);

        var result = flight.HoldSeat(seatNumber, userId, now, options.Value.ToHoldPolicy(), occupancy);
        if (result.Created)
        {
            db.SeatHolds.Add(result.Hold);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new BookingDomainException(
                $"Seat {seatNumber} on flight {flight.FlightCode} was taken by another request.", ex);
        }
        return result.Hold;
    }

    public async Task<Booking> ConfirmBookingAsync(
        Guid holdId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var hold = await db.SeatHolds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken)
            ?? throw new BookingDomainException($"Hold {holdId} not found.");

        var now = clock.GetUtcNow().UtcDateTime;
        var booking = hold.Confirm(userId, now, BookingReference.Generate()).Booking;
        db.Bookings.Add(booking);

        for (int attempt = 1; attempt <= BookingReferenceRetryAttempts; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return booking;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && IsBookingReferenceConflict(ex))
            {
                if (attempt == BookingReferenceRetryAttempts)
                {
                    throw new BookingDomainException(
                        "Could not generate a unique booking reference after several attempts.", ex);
                }
                booking.ReassignReference(BookingReference.Generate());
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new BookingDomainException(
                    $"Seat {hold.SeatNumber} was booked by another request.", ex);
            }
        }

        return booking;
    }

    public Task<Booking?> GetBookingAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

    public async Task<Booking> CancelBookingAsync(
        Guid bookingId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Flight).FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new BookingDomainException($"Booking {bookingId} not found.");

        booking.Cancel(userId, clock.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(cancellationToken);
        return booking;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolation;

    private static bool IsBookingReferenceConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
            && pg.ConstraintName is not null
            && pg.ConstraintName.Contains(nameof(Booking.BookingReference), StringComparison.OrdinalIgnoreCase);
}
