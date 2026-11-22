namespace TravelBooking.Domain.Models;

public class Flight
{
    private Flight() { }

    public Guid Id { get; private set; }
    public string FlightCode { get; private set; } = default!;
    public string Airline { get; private set; } = default!;
    public string FlightNumber { get; private set; } = default!;

    public Guid DepartureAirportId { get; private set; }
    public Airport DepartureAirport { get; private set; } = default!;

    public Guid ArrivalAirportId { get; private set; }
    public Airport ArrivalAirport { get; private set; } = default!;

    public DateTime DepartureTimeUtc { get; private set; }
    public DateTime ArrivalTimeUtc { get; private set; }

    public decimal PriceUsd { get; private set; }
    public int TotalSeats { get; private set; }

    private readonly List<SeatMapEntry> _seatMap = new();

    public IReadOnlyList<SeatMapEntry> SeatMap => _seatMap;

    public static Flight Create(
        string airline,
        string flightNumber,
        Airport departure,
        Airport arrival,
        DateTime departureTimeUtc,
        DateTime arrivalTimeUtc,
        decimal priceUsd,
        IReadOnlyList<SeatMapEntry> seatMap)
    {
        if (string.IsNullOrWhiteSpace(airline))
            throw new ArgumentException("Airline is required.", nameof(airline));
        if (string.IsNullOrWhiteSpace(flightNumber))
            throw new ArgumentException("Flight number is required.", nameof(flightNumber));
        ArgumentNullException.ThrowIfNull(departure);
        ArgumentNullException.ThrowIfNull(arrival);
        ArgumentNullException.ThrowIfNull(seatMap);
        if (departure.Id == arrival.Id)
            throw new ArgumentException("Departure and arrival airport must differ.", nameof(arrival));
        if (arrivalTimeUtc <= departureTimeUtc)
            throw new ArgumentException("Arrival must be after departure.", nameof(arrivalTimeUtc));
        if (priceUsd <= 0)
            throw new ArgumentException("Price must be greater than zero.", nameof(priceUsd));
        if (seatMap.Count == 0)
            throw new ArgumentException("Seat map must not be empty.", nameof(seatMap));
        if (seatMap.Any(s => string.IsNullOrWhiteSpace(s.Number) || s.Number != s.Number.Trim().ToUpperInvariant()))
            throw new ArgumentException("Seat numbers must be trimmed and upper-case.", nameof(seatMap));
        if (seatMap.Select(s => s.Number).Distinct(StringComparer.Ordinal).Count() != seatMap.Count)
            throw new ArgumentException("Seat map contains duplicate seat numbers.", nameof(seatMap));

        departureTimeUtc = DateTime.SpecifyKind(departureTimeUtc, DateTimeKind.Utc);
        arrivalTimeUtc = DateTime.SpecifyKind(arrivalTimeUtc, DateTimeKind.Utc);

        var flight = new Flight
        {
            Id = Guid.NewGuid(),
            FlightCode = $"{flightNumber}-{departureTimeUtc:yyyy-MM-dd}",
            Airline = airline,
            FlightNumber = flightNumber,
            DepartureAirportId = departure.Id,
            DepartureAirport = departure,
            ArrivalAirportId = arrival.Id,
            ArrivalAirport = arrival,
            DepartureTimeUtc = departureTimeUtc,
            ArrivalTimeUtc = arrivalTimeUtc,
            PriceUsd = priceUsd,
            TotalSeats = seatMap.Count,
        };
        flight._seatMap.AddRange(seatMap);
        return flight;
    }

    public bool HasSeat(string seatNumber) => SeatMap.Any(s => s.Number == seatNumber);

    public bool HasDeparted(DateTime nowUtc) => DepartureTimeUtc <= nowUtc;

    public IReadOnlyList<string> AvailableSeats(FlightOccupancy occupancy, DateTime nowUtc)
    {
        if (HasDeparted(nowUtc))
        {
            return [];
        }

        var taken = occupancy.Bookings.Where(b => b.Status == BookingStatus.Confirmed).Select(b => b.SeatNumber)
            .Concat(occupancy.Holds.Where(h => h.IsActive(nowUtc)).Select(h => h.SeatNumber))
            .ToHashSet(StringComparer.Ordinal);

        return SeatMap.Select(s => s.Number).Where(n => !taken.Contains(n)).ToList();
    }

    public HoldSeatResult HoldSeat(string seatNumber, string? userId, DateTime nowUtc, HoldPolicy policy, FlightOccupancy occupancy)
    {
        if (occupancy.Holds.Any(h => h.FlightId != Id) || occupancy.Bookings.Any(b => b.FlightId != Id))
        {
            throw new ArgumentException("Occupancy snapshot belongs to a different flight.", nameof(occupancy));
        }

        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new BookingDomainException($"A seat number is required on flight {FlightCode}.");
        }

        seatNumber = seatNumber.Trim().ToUpperInvariant();

        if (HasDeparted(nowUtc))
        {
            throw new BookingDomainException($"Flight {FlightCode} has already departed.");
        }

        if (!HasSeat(seatNumber))
        {
            throw new BookingDomainException($"Seat {seatNumber} does not exist on flight {FlightCode}.");
        }

        // Retire expired-but-still-Active holds on this seat so the unique partial index lets it be
        // re-held. Only this seat: a wider sweep races with concurrent confirmations of other seats.
        foreach (var expired in occupancy.Holds.Where(h => h.SeatNumber == seatNumber && h.Status == HoldStatus.Active && !h.IsActive(nowUtc)))
        {
            expired.MarkExpired(nowUtc);
        }

        if (occupancy.Bookings.Any(b => b.SeatNumber == seatNumber && b.Status == BookingStatus.Confirmed))
        {
            throw new BookingDomainException($"Seat {seatNumber} is already booked.");
        }

        var activeHold = occupancy.Holds.FirstOrDefault(h => h.SeatNumber == seatNumber && h.IsActive(nowUtc));
        if (activeHold is not null)
        {
            throw new BookingDomainException($"Seat {seatNumber} is currently held.");
        }

        if (userId is not null)
        {
            var heldByCaller = occupancy.Holds.Count(h => h.IsActive(nowUtc) && h.IsOwnedBy(userId));
            if (heldByCaller >= policy.MaxActiveHoldsPerCaller)
            {
                throw new BookingDomainException(
                    $"Caller already holds {heldByCaller} seats on flight {FlightCode}; the maximum is {policy.MaxActiveHoldsPerCaller}.");
            }
        }

        var expiresAtUtc = nowUtc.Add(policy.Duration);
        if (expiresAtUtc > DepartureTimeUtc)
        {
            expiresAtUtc = DepartureTimeUtc;
        }

        return new HoldSeatResult(SeatHold.Create(this, seatNumber, userId, nowUtc, expiresAtUtc), Created: true);
    }
}
