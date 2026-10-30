using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>One guest as the organizer sees them: the dishes in their cart ("Caesar Salad x2, Tom Yum x1", by dish
/// name), what the cart costs and whether it is confirmed or the guest is still choosing.</summary>
public sealed record ParticipantSummary(string Guest, string Dishes, decimal Subtotal, string Cart)
{
    public const string Confirmed = "confirmed";
    public const string StillChoosing = "still choosing";
    public const string ChangedByTheOrganizer = "changed by the organizer";

    public override string ToString() =>
        $"{Guest} | {Dishes} | {Subtotal.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} | {Cart}";
}

/// <summary>The group pre-order as the organizer sees it: the guests (the organizer's own card is not one of them),
/// the group total, the sum of every participant's subtotal (the organizer's included) and how many cards there are.</summary>
public sealed record GroupPreOrderView(
    IReadOnlyList<ParticipantSummary> Guests,
    decimal Total,
    decimal SumOfSubtotals,
    int ParticipantCount)
{
    public string DescribeGuests() => string.Join(Environment.NewLine, Guests.OrderBy(g => g.Guest, StringComparer.Ordinal));
}

/// <summary>The order of a reservation as the restaurant keeps it.</summary>
public sealed record ReservationOrder(IReadOnlyList<DishLine> Lines, OrderStatus Status);

/// <summary>What the organizer's group page says apart from the carts themselves.</summary>
public sealed record GroupPreOrderPageState(
    bool NotFound,
    string Status,
    bool CanSend,
    string ConsolidatedMessage,
    string PendingWarning);

public static class DishLines
{
    /// <summary>"Caesar Salad x2, Tom Yum x1": dishes in name order, the same dish counted once.</summary>
    public static string Describe(IEnumerable<DishLine> lines) =>
        string.Join(", ", Merge(lines).Select(line => $"{line.Dish} x{line.Quantity}"));

    /// <summary>Identical dishes as one line with the combined quantity, in dish-name order.</summary>
    public static IReadOnlyList<DishLine> Merge(IEnumerable<DishLine> lines) =>
        lines.GroupBy(line => line.Dish, StringComparer.Ordinal)
            .Select(group => new DishLine(group.Key, group.Sum(line => line.Quantity)))
            .OrderBy(line => line.Dish, StringComparer.Ordinal)
            .ToList();
}
