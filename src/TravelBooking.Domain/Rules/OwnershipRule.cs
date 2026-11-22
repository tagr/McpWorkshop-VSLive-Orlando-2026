using TravelBooking.Domain;

namespace TravelBooking.Domain.Rules;

public static class OwnershipRule
{
    public static bool Permits(string? owner, string? caller) => owner is null || caller is null || owner == caller;

    public static void EnsurePermits(string? owner, string? caller, string action)
    {
        if (!Permits(owner, caller))
        {
            throw new BookingDomainException($"Only the owner may {action}.");
        }
    }
}
