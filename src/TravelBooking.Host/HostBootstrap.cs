using Microsoft.EntityFrameworkCore;
using TravelBooking.API.Endpoints;
using TravelBooking.Infrastructure.Data;
using TravelBooking.Infrastructure.Data.Seeding;

namespace TravelBooking.Host;

internal static class HostBootstrap
{
    public static void MapTravelBookingEndpoints(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapFlightEndpoints();
        app.MapBookingEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/admin/reset-data", async (DataSeeder seeder, CancellationToken ct) =>
            {
                await seeder.ResetTransactionalDataAsync(ct);
                return Results.Ok(new { status = "reset", clearedTables = new[] { "bookings", "seat_holds", "audit_entries" } });
            });
        }
    }

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("travelbookingdb")))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelBookingDbContext>();
        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
}
