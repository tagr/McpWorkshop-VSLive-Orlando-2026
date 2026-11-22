using TravelBooking.API.Errors;
using TravelBooking.Application;
using TravelBooking.Host;
using TravelBooking.Infrastructure;
using TravelBooking.Infrastructure.Data;
using ModelContextProtocol.AspNetCore;
using TravelBooking.MCP.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<TravelBookingDbContext>(connectionName: "travelbookingdb");

builder.Services.AddTravelBookingApplication();
builder.Services.AddTravelBookingInfrastructure(builder.Configuration);

builder.Services.AddExceptionHandler<BookingDomainExceptionHandler>();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients)
    .WithToolsFromAssembly(typeof(FlightTools).Assembly)
    .WithResourcesFromAssembly(typeof(FlightTools).Assembly)
    .WithPromptsFromAssembly(typeof(FlightTools).Assembly);

var app = builder.Build();

app.UseExceptionHandler();

app.UseCors();
app.MapMcp("/mcp");

app.MapTravelBookingEndpoints();

await app.MigrateAndSeedAsync();

app.Run();
