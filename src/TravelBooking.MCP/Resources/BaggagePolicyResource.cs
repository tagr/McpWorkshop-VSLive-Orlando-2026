using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TravelBooking.MCP.Resources;

/// <summary>
/// The one MCP <em>resource</em> of the workshop server: static reference text a client can
/// read and hand to the model as context. Tools do things; this just informs.
/// </summary>
[McpServerResourceType]
public static class BaggagePolicyResource
{
    public const string Uri = "travel://policies/baggage";
    public const string Name = "baggage-policy";
    public const string Title = "Baggage policy";
    public const string MimeType = "text/markdown";

    public const string Markdown = """
        # Baggage policy

        This is the fictional baggage policy of the Travel Booking workshop. It exists to give
        an assistant something to read before a traveller commits to a seat.
        The same policy applies to every flight in this demo.

        ## Cabin baggage

        - One cabin bag of at most 55 x 40 x 23 cm and 8 kg, stored in the overhead locker.
        - One personal item (handbag, laptop bag or small backpack) that fits under the seat in front.
        - Liquids in containers of at most 100 ml, together in one transparent 1-litre bag.

        ## Checked baggage

        - Every ticket includes one checked bag of at most 23 kg and 158 cm (length + width + height).
        - A second checked bag can be added for USD 60 per flight; a third for USD 120.
        - Bags between 23 kg and 32 kg carry a heavy-bag fee of USD 75. Bags over 32 kg are not accepted.

        ## Oversized items

        - Anything longer than 158 cm in total, such as skis, golf bags, bicycles or musical instruments, counts as oversized.
        - Oversized items replace one checked bag and carry a surcharge of USD 100 per flight.
        - Book oversized items at least 48 hours before departure; space on board is limited.
        """;

    [McpServerResource(UriTemplate = Uri, Name = Name, Title = Title, MimeType = MimeType)]
    [Description("The workshop's fictional baggage policy: cabin, checked and oversized allowances " +
        "that apply to every flight in this demo. Read it before a traveller commits to a booking.")]
    public static TextResourceContents Read() => new()
    {
        Uri = Uri,
        MimeType = MimeType,
        Text = Markdown,
    };
}
