using TravelBooking.Domain.Rules;

namespace TravelBooking.Domain.Models;

public class SeatHold
{
    public Guid Id { get; private set; }

    public Guid FlightId { get; private set; }
    public Flight Flight { get; private set; } = default!;

    public string SeatNumber { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public HoldStatus Status { get; private set; }

    public Guid? ConfirmedAsBookingId { get; private set; }
    public Booking? ConfirmedAsBooking { get; private set; }

    public string? UserId { get; private set; }

    private SeatHold() { }

    internal static SeatHold Create(Flight flight, string seatNumber, string? userId, DateTime nowUtc, DateTime expiresAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            FlightId = flight.Id,
            Flight = flight,
            SeatNumber = seatNumber,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            Status = HoldStatus.Active,
        };

    public bool IsActive(DateTime nowUtc) => Status == HoldStatus.Active && ExpiresAtUtc > nowUtc;

    public bool IsOwnedBy(string? userId) => UserId is not null && UserId == userId;

    public ConfirmResult Confirm(string? userId, DateTime nowUtc, string bookingReference)
    {
        if (Status != HoldStatus.Active)
        {
            throw new BookingDomainException($"Hold {Id} is not active (status: {Status}).");
        }

        if (ExpiresAtUtc <= nowUtc)
        {
            throw new BookingDomainException($"Hold {Id} expired at {ExpiresAtUtc:o}.");
        }

        OwnershipRule.EnsurePermits(UserId, userId, "confirm this hold");

        var booking = Booking.Create(this, userId ?? UserId, nowUtc, bookingReference);
        Status = HoldStatus.Confirmed;
        ConfirmedAsBookingId = booking.Id;
        ConfirmedAsBooking = booking;
        return new ConfirmResult(booking, Created: true);
    }

    public void MarkExpired(DateTime nowUtc)
    {
        if (Status != HoldStatus.Active || ExpiresAtUtc > nowUtc)
        {
            throw new InvalidOperationException("Only an active hold that has passed its expiry can be marked expired.");
        }

        Status = HoldStatus.Expired;
    }
}

public enum HoldStatus
{
    Active = 0,
    Confirmed = 1,
    Expired = 2,
}
