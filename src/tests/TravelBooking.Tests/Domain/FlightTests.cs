using AwesomeAssertions.Execution;
using TravelBooking.Domain;
using TravelBooking.Domain.Models;

namespace TravelBooking.Tests.Domain;

public class FlightTests
{
    private static readonly Airport Lhr = Airport.Create("LHR", "London", "United Kingdom");
    private static readonly Airport Jfk = Airport.Create("JFK", "New York", "United States");
    private static readonly DateTime Dep = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = Dep.AddDays(-1);
    private static readonly HoldPolicy Policy = new(TimeSpan.FromMinutes(5), 5);

    private static readonly IReadOnlyList<SeatMapEntry> SixSeats =
        "ABCDEF".Select(c => new SeatMapEntry($"1{c}", 1, c.ToString())).ToList();

    private static Flight NewFlight(DateTime? departure = null)
    {
        var dep = departure ?? Dep;
        return Flight.Create("British Airways", "BA117", Lhr, Jfk, dep, dep.AddHours(7), 500m, SixSeats);
    }

    [Fact]
    public void Create_derives_flight_code_and_total_seats()
    {
        var flight = Flight.Create("British Airways", "BA117", Lhr, Jfk, Dep, Dep.AddHours(7), 500m, SixSeats);

        using var _ = new AssertionScope();
        flight.FlightCode.Should().Be($"BA117-{Dep:yyyy-MM-dd}");
        flight.TotalSeats.Should().Be(6);
        flight.DepartureTimeUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Create_rejects_same_departure_and_arrival_airport()
    {
        var act = () => Flight.Create("British Airways", "BA117", Lhr, Lhr, Dep, Dep.AddHours(7), 500m, SixSeats);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_arrival_before_departure()
    {
        var act = () => Flight.Create("British Airways", "BA117", Lhr, Jfk, Dep, Dep.AddHours(-1), 500m, SixSeats);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_duplicate_seat_numbers()
    {
        var seats = new List<SeatMapEntry> { new("1A", 1, "A"), new("1A", 1, "A") };

        var act = () => Flight.Create("British Airways", "BA117", Lhr, Jfk, Dep, Dep.AddHours(7), 500m, seats);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(" 1A")]
    [InlineData("1a")]
    public void Create_rejects_non_canonical_seat_numbers(string seatNumber)
    {
        var seats = new List<SeatMapEntry> { new(seatNumber, 1, "A"), new("1B", 1, "B") };

        var act = () => Flight.Create("British Airways", "BA117", Lhr, Jfk, Dep, Dep.AddHours(7), 500m, seats);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HasDeparted_is_true_at_and_after_departure_time()
    {
        var flight = NewFlight(departure: Dep);

        using var _ = new AssertionScope();
        flight.HasDeparted(Dep.AddSeconds(-1)).Should().BeFalse();
        flight.HasDeparted(Dep).Should().BeTrue();
    }

    private static SeatHold ActiveHold(Flight flight, string seat, string? userId) =>
        flight.HoldSeat(seat, userId, Now, Policy, FlightOccupancy.Empty).Hold;

    [Fact]
    public void HoldSeat_creates_active_hold_that_expires_after_policy_duration()
    {
        var result = NewFlight().HoldSeat("1A", null, Now, Policy, FlightOccupancy.Empty);

        using var _ = new AssertionScope();
        result.Created.Should().BeTrue();
        result.Hold.Status.Should().Be(HoldStatus.Active);
        result.Hold.ExpiresAtUtc.Should().Be(Now.AddMinutes(5));
        result.Hold.FlightId.Should().Be(result.Hold.Flight.Id);
    }

    [Fact]
    public void HoldSeat_normalises_seat_number()
        => NewFlight().HoldSeat(" 1a ", null, Now, Policy, FlightOccupancy.Empty).Hold.SeatNumber.Should().Be("1A");

    [Fact]
    public void HoldSeat_rejects_unknown_seat()
    {
        var act = () => NewFlight().HoldSeat("99Z", null, Now, Policy, FlightOccupancy.Empty);

        act.Should().Throw<BookingDomainException>().WithMessage("*99Z*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HoldSeat_rejects_missing_seat_number(string? seatNumber)
    {
        var act = () => NewFlight().HoldSeat(seatNumber!, null, Now, Policy, FlightOccupancy.Empty);

        act.Should().Throw<BookingDomainException>().WithMessage("*seat number is required*");
    }

    [Fact]
    public void HoldSeat_rejects_departed_flight()
    {
        var act = () => NewFlight().HoldSeat("1A", null, Dep, Policy, FlightOccupancy.Empty);

        act.Should().Throw<BookingDomainException>().WithMessage("*already departed*");
    }

    [Fact]
    public void HoldSeat_caps_expiry_at_departure()
    {
        var flight = NewFlight(departure: Now.AddMinutes(2));

        var result = flight.HoldSeat("1A", null, Now, Policy, FlightOccupancy.Empty);

        result.Hold.ExpiresAtUtc.Should().Be(flight.DepartureTimeUtc);
    }

    [Fact]
    public void HoldSeat_rejects_seat_held_by_someone_else()
    {
        var flight = NewFlight();
        var alice = ActiveHold(flight, "1A", "alice");

        var act = () => flight.HoldSeat("1A", "bob", Now, Policy, new FlightOccupancy([alice], []));

        act.Should().Throw<BookingDomainException>().WithMessage("*currently held*");
    }

    [Fact]
    public void HoldSeat_rejects_anonymous_re_hold()
    {
        var flight = NewFlight();
        var anonymous = ActiveHold(flight, "1A", null);

        var act = () => flight.HoldSeat("1A", null, Now, Policy, new FlightOccupancy([anonymous], []));

        act.Should().Throw<BookingDomainException>().WithMessage("*currently held*");
    }

    [Fact]
    public void HoldSeat_rejects_seat_with_confirmed_booking()
    {
        var flight = NewFlight();
        var confirmedHold = flight.HoldSeat("1A", "alice", Now, Policy, FlightOccupancy.Empty).Hold;
        var booking = confirmedHold.Confirm("alice", Now, "BKG-TEST01").Booking;

        var act = () => flight.HoldSeat("1A", null, Now, Policy, new FlightOccupancy([], [booking]));

        act.Should().Throw<BookingDomainException>().WithMessage("*already booked*");
    }

    [Fact]
    public void HoldSeat_reports_booked_before_held()
    {
        var flight = NewFlight();
        var hold = flight.HoldSeat("1A", "alice", Now, Policy, FlightOccupancy.Empty).Hold;
        var booking = hold.Confirm("alice", Now, "BKG-TEST01").Booking;

        var act = () => flight.HoldSeat("1A", "bob", Now, Policy, new FlightOccupancy([hold], [booking]));

        act.Should().Throw<BookingDomainException>().WithMessage("*already booked*");
    }

    [Fact]
    public void HoldSeat_marks_expired_holds_and_allows_re_hold()
    {
        var flight = NewFlight();
        var old = flight.HoldSeat("1A", "alice", Now, Policy, FlightOccupancy.Empty).Hold;
        var later = Now.AddMinutes(6);

        var result = flight.HoldSeat("1A", "bob", later, Policy, new FlightOccupancy([old], []));

        using var _ = new AssertionScope();
        old.Status.Should().Be(HoldStatus.Expired);
        result.Created.Should().BeTrue();
    }

    [Fact]
    public void HoldSeat_leaves_expired_holds_on_other_seats_untouched()
    {
        var flight = NewFlight();
        var other = flight.HoldSeat("1B", "alice", Now, Policy, FlightOccupancy.Empty).Hold;
        var later = Now.AddMinutes(6);

        var result = flight.HoldSeat("1A", "bob", later, Policy, new FlightOccupancy([other], []));

        using var _ = new AssertionScope();
        result.Created.Should().BeTrue();
        other.Status.Should().Be(HoldStatus.Active);
    }

    [Fact]
    public void HoldSeat_rejects_sixth_hold_for_same_named_caller()
    {
        var flight = NewFlight();
        var holds = "ABCDE".Select(c => ActiveHold(flight, $"1{c}", "alice")).ToList();

        var act = () => flight.HoldSeat("1F", "alice", Now, Policy, new FlightOccupancy(holds, []));

        act.Should().Throw<BookingDomainException>().WithMessage("*maximum is 5*");
    }

    [Fact]
    public void HoldSeat_does_not_cap_anonymous_callers()
    {
        var flight = NewFlight();
        var holds = "ABCDE".Select(c => ActiveHold(flight, $"1{c}", null)).ToList();

        var result = flight.HoldSeat("1F", null, Now, Policy, new FlightOccupancy(holds, []));

        result.Created.Should().BeTrue();
    }

    [Fact]
    public void HoldSeat_rejects_occupancy_from_another_flight()
    {
        var other = NewFlight();
        var foreign = ActiveHold(other, "1A", "alice");

        var act = () => NewFlight().HoldSeat("1B", "alice", Now, Policy, new FlightOccupancy([foreign], []));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AvailableSeats_excludes_booked_and_actively_held_seats()
    {
        var flight = NewFlight();
        var booking = flight.HoldSeat("1A", "alice", Now, Policy, FlightOccupancy.Empty).Hold
            .Confirm("alice", Now.AddMinutes(1), "BKG-TEST01").Booking;
        var hold = flight.HoldSeat("1B", "bob", Now, Policy, FlightOccupancy.Empty).Hold;

        var available = flight.AvailableSeats(new FlightOccupancy([hold], [booking]), Now.AddMinutes(1));

        available.Should().Equal("1C", "1D", "1E", "1F");
    }

    [Fact]
    public void AvailableSeats_includes_seats_whose_hold_expired()
    {
        var flight = NewFlight();
        var hold = flight.HoldSeat("1B", "bob", Now, Policy, FlightOccupancy.Empty).Hold;

        var available = flight.AvailableSeats(new FlightOccupancy([hold], []), hold.ExpiresAtUtc);

        available.Should().Contain("1B").And.HaveCount(6);
    }

    [Fact]
    public void AvailableSeats_is_empty_once_flight_departed()
    {
        var flight = NewFlight();

        flight.AvailableSeats(FlightOccupancy.Empty, Dep).Should().BeEmpty();
    }
}
