using AwesomeAssertions.Execution;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TravelBooking.Domain;
using TravelBooking.Domain.Models;
using TravelBooking.Infrastructure.Services;
using TravelBooking.Tests.TestHelpers;

namespace TravelBooking.Tests.Infrastructure;

public class BookingServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private (BookingService service, FakeTimeProvider clock, TravelBooking.Infrastructure.Data.TravelBookingDbContext db, Flight flight)
        Setup(int holdMinutes = 5, int maxHolds = 5)
    {
        var db = TestDb.NewContext();
        var lhr = TestData.NewAirport("LHR", "London", "United Kingdom");
        var jfk = TestData.NewAirport("JFK", "New York", "United States");
        var flight = TestData.NewFlight(lhr, jfk, Now.AddDays(1));
        db.Airports.AddRange(lhr, jfk);
        db.Flights.Add(flight);
        db.SaveChanges();

        var clock = new FakeTimeProvider(Now);
        var opts = Options.Create(new BookingOptions { SeatHoldDurationMinutes = holdMinutes, MaxActiveHoldsPerCaller = maxHolds });
        return (new BookingService(db, clock, opts), clock, db, flight);
    }

    [Fact]
    public async Task HoldSeat_creates_active_hold_with_correct_expiry()
    {
        var (svc, _, _, flight) = Setup(holdMinutes: 5);

        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        using var _ = new AssertionScope();
        hold.Status.Should().Be(HoldStatus.Active);
        hold.SeatNumber.Should().Be("1A");
        hold.ExpiresAtUtc.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public async Task HoldSeat_rejects_unknown_seat()
    {
        var (svc, _, _, flight) = Setup();

        var act = async () => await svc.HoldSeatAsync(flight.Id, "99Z", userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>()
            .WithMessage("*99Z*");
    }

    [Fact]
    public async Task HoldSeat_rejects_unknown_flight()
    {
        var (svc, _, _, _) = Setup();

        var act = async () => await svc.HoldSeatAsync(Guid.NewGuid(), "1A", userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task HoldSeat_rejects_when_seat_already_held_and_not_expired()
    {
        var (svc, _, _, flight) = Setup();
        await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        var act = async () => await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task HoldSeat_allows_re_hold_after_previous_hold_expired()
    {
        var (svc, clock, _, flight) = Setup(holdMinutes: 5);
        await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(6));

        var second = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        second.Status.Should().Be(HoldStatus.Active);
    }

    [Fact]
    public async Task ConfirmBooking_creates_booking_and_marks_hold_confirmed()
    {
        var (svc, _, db, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        var booking = await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);

        var reloadedHold = db.SeatHolds.Single(h => h.Id == hold.Id);

        using var _ = new AssertionScope();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.SeatNumber.Should().Be("1A");
        booking.BookingReference.Should().StartWith("BKG-");
        reloadedHold.Status.Should().Be(HoldStatus.Confirmed);
        reloadedHold.ConfirmedAsBookingId.Should().Be(booking.Id);
    }

    [Fact]
    public async Task ConfirmBooking_rejects_expired_hold()
    {
        var (svc, clock, _, flight) = Setup(holdMinutes: 5);
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));

        var act = async () => await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task ConfirmBooking_rejects_unknown_hold()
    {
        var (svc, _, _, _) = Setup();

        var act = async () => await svc.ConfirmBookingAsync(Guid.NewGuid(), userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task CancelBooking_marks_booking_cancelled_and_frees_seat()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);
        var booking = await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);

        var cancelled = await svc.CancelBookingAsync(booking.Id, userId: null, CancellationToken.None);
        var newHold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        using var _ = new AssertionScope();
        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.CancelledAtUtc.Should().NotBeNull();
        newHold.Status.Should().Be(HoldStatus.Active);
    }

    [Fact]
    public async Task CancelBooking_rejects_already_cancelled()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);
        var booking = await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);
        await svc.CancelBookingAsync(booking.Id, userId: null, CancellationToken.None);

        var act = async () => await svc.CancelBookingAsync(booking.Id, userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task GetBooking_returns_null_for_unknown_id()
    {
        var (svc, _, _, _) = Setup();

        var booking = await svc.GetBookingAsync(Guid.NewGuid(), CancellationToken.None);

        booking.Should().BeNull();
    }

    [Fact]
    public async Task HoldSeat_rejects_when_seat_is_already_booked()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);
        await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);

        var act = async () => await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task HoldSeat_records_userId_when_supplied()
    {
        var (svc, _, _, flight) = Setup();

        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: "user-42", CancellationToken.None);

        hold.UserId.Should().Be("user-42");
    }

    [Fact]
    public async Task HoldSeat_rejects_flight_that_has_departed()
    {
        var (svc, clock, _, flight) = Setup();
        clock.Advance(TimeSpan.FromDays(2));

        var act = async () => await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>().WithMessage("*departed*");
    }

    [Fact]
    public async Task HoldSeat_rejects_sixth_hold_by_same_user()
    {
        var (svc, _, _, flight) = Setup();
        foreach (var seat in new[] { "1A", "1B", "1C", "1D", "1E" })
        {
            await svc.HoldSeatAsync(flight.Id, seat, userId: "user-42", CancellationToken.None);
        }

        var act = async () => await svc.HoldSeatAsync(flight.Id, "1F", userId: "user-42", CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>().WithMessage("*maximum is 5*");
    }

    [Fact]
    public async Task ConfirmBooking_rejects_a_different_user()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: "user-42", CancellationToken.None);

        var act = async () => await svc.ConfirmBookingAsync(hold.Id, userId: "user-43", CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>().WithMessage("*owner*");
    }

    [Fact]
    public async Task CancelBooking_rejects_a_different_user()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: "user-42", CancellationToken.None);
        var booking = await svc.ConfirmBookingAsync(hold.Id, userId: "user-42", CancellationToken.None);

        var act = async () => await svc.CancelBookingAsync(booking.Id, userId: "user-43", CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>().WithMessage("*owner*");
    }

    [Fact]
    public async Task ConfirmBooking_retry_by_a_different_user_is_rejected()
    {
        var (svc, _, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: "user-42", CancellationToken.None);
        await svc.ConfirmBookingAsync(hold.Id, userId: "user-42", CancellationToken.None);

        // Before Lab 4 the retry fails as "not active", after it the ownership rule rejects it; either way the other caller never receives the booking.
        var act = async () => await svc.ConfirmBookingAsync(hold.Id, userId: "user-43", CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>();
    }

    [Fact]
    public async Task CancelBooking_rejects_after_departure()
    {
        var (svc, clock, _, flight) = Setup();
        var hold = await svc.HoldSeatAsync(flight.Id, "1A", userId: null, CancellationToken.None);
        var booking = await svc.ConfirmBookingAsync(hold.Id, userId: null, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(2));

        var act = async () => await svc.CancelBookingAsync(booking.Id, userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<BookingDomainException>().WithMessage("*departed*");
    }
}
