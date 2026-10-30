using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

/// <summary>Who takes part in a group pre-order scenario. Accounts are remembered under these names, so a step can
/// act as "the organizer" or "another customer" without knowing how that person signed in.</summary>
public static class Personas
{
    public const string Organizer = "the organizer";
    public const string AnotherOrganizer = "another organizer";
    public const string Waiter = "the waiter";
    public const string AnotherCustomer = "another customer";
    public const string GuestOfTheGroup = "a guest of the group";
    public const string AnonymousVisitor = "an anonymous visitor";
}

/// <summary>A registered account and the id token it signed in with.</summary>
public sealed record Account(string Email, string Password, string FirstName, string IdToken);

/// <summary>The restaurant a scenario set up: its location, the table groups book and the dishes on its menu,
/// keyed by the name the feature file uses (the stored name carries the "AQA-Sample-" prefix).</summary>
public sealed record Restaurant(
    string LocationId,
    string LocationAddress,
    string TableId,
    IReadOnlyDictionary<string, DishResponse> Dishes)
{
    public DishResponse Dish(string name) =>
        Dishes.TryGetValue(name, out var dish)
            ? dish
            : throw new InvalidOperationException(
                $"The restaurant has no dish called '{name}'. Its menu: {string.Join(", ", Dishes.Keys)}.");

    /// <summary>The name a feature file uses for a dish the application shows with the test-data prefix.</summary>
    public static string PlainName(string storedName)
    {
        var name = storedName.Trim();
        return name.StartsWith(SampleDataFactory.Prefix, StringComparison.Ordinal) ? name[SampleDataFactory.Prefix.Length..] : name;
    }
}

/// <summary>A reservation that became a group: the reservation, its group and the invitation guests open.</summary>
public sealed record GroupBooking(string ReservationId, string GroupReservationId, string InviteToken);

/// <summary>A guest's place in a group: their session and the token that stands in for a sign-in.</summary>
public sealed record GuestCart(string Name, string Persona, string SessionId, string SessionToken, bool Confirmed);

/// <summary>The organizer's own session in the group (the "My Pre-Order" card).</summary>
public sealed record OwnSession(string GuestSessionId);

/// <summary>One dish line: a dish (by its plain name) and how many.</summary>
public sealed record DishLine(string Dish, int Quantity);
