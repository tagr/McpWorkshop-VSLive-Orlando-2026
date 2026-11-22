using AwesomeAssertions.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using TravelBooking.Domain.Services;
using TravelBooking.MCP.Prompts;
using TravelBooking.MCP.Resources;
using TravelBooking.Tests.TestHelpers;

namespace TravelBooking.Tests.Mcp;

/// <summary>
/// Drives the MCP server the way a client does, over an in-process transport, so the
/// three primitives are checked on the wire: what is advertised, what a read returns,
/// and that fetching the prompt executes nothing.
/// </summary>
public class McpServerPrimitivesTests
{
    [Fact]
    public async Task ListResources_advertises_the_baggage_policy()
    {
        await using var mcp = await InProcessMcp.StartAsync();
        var resources = await mcp.Client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        resources.Should().ContainSingle();
        resources[0].Uri.Should().Be("travel://policies/baggage");
        resources[0].Name.Should().Be("baggage-policy");
        resources[0].Title.Should().Be("Baggage policy");
        resources[0].MimeType.Should().Be("text/markdown");
        resources[0].Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReadResource_returns_the_baggage_policy_as_markdown()
    {
        await using var mcp = await InProcessMcp.StartAsync();
        var result = await mcp.Client.ReadResourceAsync("travel://policies/baggage",
            cancellationToken: TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        var text = result.Contents.Should().ContainSingle().Which.Should().BeOfType<TextResourceContents>().Subject;
        text.Uri.Should().Be("travel://policies/baggage");
        text.MimeType.Should().Be("text/markdown");
        text.Text.Should().Contain("## Cabin baggage")
            .And.Contain("## Checked baggage")
            .And.Contain("## Oversized items")
            .And.Contain("The same policy applies to every flight in this demo.");
    }

    [Fact]
    public async Task ListPrompts_advertises_book_flight_with_three_required_arguments()
    {
        await using var mcp = await InProcessMcp.StartAsync();
        var prompts = await mcp.Client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        var prompt = prompts.Should().ContainSingle().Subject.ProtocolPrompt;
        prompt.Name.Should().Be("book-flight");
        prompt.Title.Should().Be("Book a flight");
        prompt.Description.Should().NotBeNullOrWhiteSpace();
        prompt.Arguments.Should().HaveCount(3);
        prompt.Arguments!.Select(a => a.Name).Should().Equal("from", "to", "date");
        prompt.Arguments.Should().OnlyContain(a => a.Required == true);
        prompt.Arguments.Single(a => a.Name == "date").Description.Should().Contain("YYYY-MM-DD");
    }

    [Fact]
    public async Task GetPrompt_returns_the_booking_workflow_and_embeds_the_baggage_policy()
    {
        await using var mcp = await InProcessMcp.StartAsync();
        var result = await mcp.Client.GetPromptAsync("book-flight",
            new Dictionary<string, object?> { ["from"] = "AMS", ["to"] = "LHR", ["date"] = "2026-11-20" },
            cancellationToken: TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        result.Messages.Should().HaveCount(2);
        result.Messages.Should().OnlyContain(m => m.Role == Role.User);
        var text = result.Messages[0].Content.Should().BeOfType<TextContentBlock>().Subject.Text;
        text.Should().Contain("AMS").And.Contain("LHR").And.Contain("2026-11-20");
        var last = -1;
        foreach (var token in new[] { "SearchFlights", "GetFlightDetails", "baggage policy included in this prompt", "HoldSeat", "ConfirmBooking" })
        {
            var index = text.IndexOf(token, StringComparison.Ordinal);
            index.Should().BeGreaterThan(last, $"{token} must come after the previous step");
            last = index;
        }
        text.Should().Contain("expir", "the assistant must relay the hold expiry and handle an expired hold");
        text.Should().Contain("never invent", "the assistant must use only tool-returned data");
        text.Should().Contain("If no flights come back", "an empty search is where a model is most tempted to invent");
        text.Should().Contain("as UTC", "ExpiresAtUtc is UTC and the prompt must not have the model guess a time zone")
            .And.NotContain("their own time zone");
        var policy = result.Messages[1].Content.Should().BeOfType<EmbeddedResourceBlock>()
            .Which.Resource.Should().BeOfType<TextResourceContents>().Subject;
        policy.Uri.Should().Be("travel://policies/baggage");
        policy.MimeType.Should().Be("text/markdown");
        policy.Text.Should().Be(BaggagePolicyResource.Markdown);
        result.Messages.Select(m => m.Content).OfType<TextContentBlock>().Should().OnlyContain(
            t => !t.Text.Contains("read the resource", StringComparison.OrdinalIgnoreCase)
                && !t.Text.Contains("travel://policies/baggage", StringComparison.Ordinal),
            "resources are application-controlled; the model gets the policy embedded, not a URI to read");
    }

    [Fact]
    public async Task GetPrompt_passes_arguments_through_unchanged_and_calls_no_service()
    {
        var flights = new Mock<IFlightService>();
        var bookings = new Mock<IBookingService>();
        var audit = new Mock<IAuditService>();
        await using var mcp = await InProcessMcp.StartAsync(services =>
        {
            services.AddSingleton(flights.Object);
            services.AddSingleton(bookings.Object);
            services.AddSingleton(audit.Object);
        });

        var result = await mcp.Client.GetPromptAsync("book-flight",
            new Dictionary<string, object?> { ["from"] = " ams ", ["to"] = "london", ["date"] = "20/11/2026" },
            cancellationToken: TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        var text = result.Messages[0].Content.Should().BeOfType<TextContentBlock>().Subject.Text;
        text.Should().Contain(" ams ").And.Contain("london").And.Contain("20/11/2026");
        text.Should().Contain("ask the user", "malformed arguments are the user's to fix, not the prompt's");
        flights.VerifyNoOtherCalls();
        bookings.VerifyNoOtherCalls();
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListTools_still_advertises_the_six_booking_tools()
    {
        await using var mcp = await InProcessMcp.StartAsync();
        var tools = await mcp.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "SearchFlights", "GetFlightDetails", "HoldSeat", "ConfirmBooking", "GetBooking", "CancelBooking");
    }
}
