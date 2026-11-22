using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TravelBooking.MCP.Resources;

namespace TravelBooking.MCP.Prompts;

/// <summary>
/// The one MCP <em>prompt</em> of the workshop server: a reusable workflow the client can
/// drop into a conversation. It only returns messages. It never calls a tool, holds a seat
/// or creates a booking; the assistant does that, one tool at a time, with the user.
/// </summary>
[McpServerPromptType]
public static class BookFlightPrompt
{
    public const string Name = "book-flight";

    [McpServerPrompt(Name = Name, Title = "Book a flight")]
    [Description("Guides the assistant through booking one seat with the Travel Booking tools: " +
        "search, pick a flight and a seat, check the baggage policy, hold, confirm.")]
    public static IEnumerable<PromptMessage> Create(
        [Description("IATA code of the departure airport, e.g. 'AMS'.")] string from,
        [Description("IATA code of the destination airport, e.g. 'LHR'.")] string to,
        [Description("Travel date in YYYY-MM-DD format, e.g. '2026-11-20'.")] string date)
    {
        var instructions = $"""
            Help the user book a seat from {from} to {to} on {date}.
            If any of these values looks missing or malformed, ask the user to correct it before searching.

            Work through these steps in order, one tool call at a time:

            1. Call SearchFlights with the departure airport, destination airport and date above.
               If no flights come back, say so and ask the user for another date or route; do not
               search again on your own.
            2. If more than one flight comes back, ask the user which one they want. Then call
               GetFlightDetails with that flight's id to get its available seats.
            3. Explain the baggage allowance that matters for this trip (cabin, checked, oversized)
               from the baggage policy included in this prompt, before the user commits.
            4. Ask the user to pick one of the available seats returned by GetFlightDetails.
            5. Call HoldSeat with the flight id and the chosen seat number. Tell the user the exact
               expiration time the hold returns, stated as UTC; convert it only if the user has
               told you their time zone.
            6. Ask the user for explicit confirmation before calling ConfirmBooking with the hold id.
            7. Show the booking reference from the result. If ConfirmBooking fails because the hold
               expired, obtain a fresh hold with HoldSeat and a fresh confirmation; never assume the
               old hold is still valid.

            Use only the flight ids, seat numbers, prices and availability the tools return;
            never invent flights, seats, prices or successful bookings.
            """;

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = instructions },
            },
            // Embedded, not linked: resources are application-controlled, so the model cannot
            // be counted on to read a URI by itself. The host receives the policy with the prompt.
            new PromptMessage
            {
                Role = Role.User,
                Content = new EmbeddedResourceBlock { Resource = BaggagePolicyResource.Read() },
            },
        ];
    }
}
