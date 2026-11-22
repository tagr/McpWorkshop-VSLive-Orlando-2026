using AwesomeAssertions.Execution;
using TravelBooking.Domain;
using TravelBooking.Domain.Models;

namespace TravelBooking.Tests.Domain;

public class SeatHoldTests
{
    private static readonly Airport Lhr = Airport.Create("LHR", "London", "United Kingdom");
    private static readonly Airport Jfk = Airport.Create("JFK", "New York", "United States");
    private static readonly DateTime Dep = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Dep.AddDays(-1);
    private static readonly HoldPolicy Policy = new(TimeSpan.FromMinutes(5), 5);

    private static SeatHold NewHold(string? userId)
    {
        var seats = new List<SeatMapEntry> { new("1A", 1, "A") };
        var flight = Flight.Create("British Airways", "BA117", Lhr, Jfk, Dep, Dep.AddHours(7), 500m, seats);
        return flight.HoldSeat("1A", userId, Now, Policy, FlightOccupancy.Empty).Hold;
    }

    [Fact]
    public void IsActive_is_false_at_the_expiry_instant()
    {
        var hold = NewHold("alice");

        using var _ = new AssertionScope();
        hold.IsActive(hold.ExpiresAtUtc.AddTicks(-1)).Should().BeTrue();
        hold.IsActive(hold.ExpiresAtUtc).Should().BeFalse();
    }

    [Fact]
    public void IsOwnedBy_is_false_for_anonymous_holds()
    {
        var hold = NewHold(null);

        hold.IsOwnedBy(null).Should().BeFalse();
    }

    [Fact]
    public void MarkExpired_rejects_a_hold_that_has_not_expired()
    {
        var hold = NewHold("alice");

        var act = () => hold.MarkExpired(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkExpired_sets_status_expired()
    {
        var hold = NewHold("alice");

        hold.MarkExpired(hold.ExpiresAtUtc);

        hold.Status.Should().Be(HoldStatus.Expired);
    }

    [Fact]
    public void Confirm_creates_confirmed_booking_and_links_the_hold()
    {
        var hold = NewHold("alice");

        var booking = hold.Confirm("alice", Now.AddMinutes(1), "BKG-TEST01").Booking;

        using var _ = new AssertionScope();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.BookingReference.Should().Be("BKG-TEST01");
        booking.SeatNumber.Should().Be(hold.SeatNumber);
        booking.UserId.Should().Be("alice");
        hold.Status.Should().Be(HoldStatus.Confirmed);
        hold.ConfirmedAsBookingId.Should().Be(booking.Id);
    }

    [Fact]
    public void Confirm_rejects_expired_hold()
    {
        var hold = NewHold("alice");

        var act = () => hold.Confirm("alice", hold.ExpiresAtUtc, "BKG-TEST01");

        act.Should().Throw<BookingDomainException>().WithMessage("*expired*");
    }

    [Fact]
    public void Confirm_rejects_hold_that_is_not_active()
    {
        var hold = NewHold("alice");
        hold.Confirm("alice", Now.AddMinutes(1), "BKG-TEST01");

        var act = () => hold.Confirm("alice", Now.AddMinutes(2), "BKG-TEST02");

        act.Should().Throw<BookingDomainException>().WithMessage("*not active*");
    }

    [Fact]
    public void Confirm_rejects_a_different_named_caller()
    {
        var hold = NewHold("alice");

        var act = () => hold.Confirm("bob", Now.AddMinutes(1), "BKG-TEST01");

        act.Should().Throw<BookingDomainException>().WithMessage("Only the owner may confirm this hold.");
    }

    [Fact]
    public void Confirm_falls_back_to_the_holds_owner_when_caller_is_anonymous_and_unowned()
    {
        var hold = NewHold(null);

        var booking = hold.Confirm(null, Now.AddMinutes(1), "BKG-TEST01").Booking;

        booking.UserId.Should().BeNull();
    }
}
