using System.Globalization;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Support;

/// <summary>`| dish | price |`: the dishes a scenario's restaurant has on its menu.</summary>
public sealed class Menu(IReadOnlyList<(string Dish, decimal Price)> dishes)
{
    public IReadOnlyList<(string Dish, decimal Price)> Dishes { get; } = dishes;

    public static Menu From(DataTable table) =>
        new(table.Rows.Select(row => (row["dish"], decimal.Parse(row["price"], CultureInfo.InvariantCulture))).ToList());
}

/// <summary>`| dish | quantity |`: dishes someone puts in a cart, or expects to find in an order.</summary>
public sealed class DishOrder(IReadOnlyList<DishLine> lines)
{
    public IReadOnlyList<DishLine> Lines { get; } = lines;

    public static DishOrder From(DataTable table) =>
        new(table.Rows.Select(row => new DishLine(row["dish"], int.Parse(row["quantity"], CultureInfo.InvariantCulture))).ToList());

    /// <summary>One line per dish, in dish-name order, for comparing with what the application shows.</summary>
    public string Describe() => DishLines.Describe(Lines);
}

/// <summary>`| guest | dishes | subtotal | cart |`: the guests the organizer is expected to see.</summary>
public sealed class ExpectedGuests(IReadOnlyList<ParticipantSummary> guests)
{
    public IReadOnlyList<ParticipantSummary> Guests { get; } = guests;

    public static ExpectedGuests From(DataTable table) =>
        new(table.Rows.Select(row => new ParticipantSummary(
            row["guest"],
            DishLines.Describe(ParseDishes(row["dishes"])),
            decimal.Parse(row["subtotal"], CultureInfo.InvariantCulture),
            row["cart"])).ToList());

    public string Describe() => string.Join(Environment.NewLine, Guests.OrderBy(g => g.Guest, StringComparer.Ordinal));

    /// <summary>"Caesar Salad x2, Tom Yum x1" (empty for no dishes).</summary>
    private static IEnumerable<DishLine> ParseDishes(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part =>
        {
            var separator = part.LastIndexOf(" x", StringComparison.Ordinal);
            return new DishLine(part[..separator].Trim(), int.Parse(part[(separator + 2)..], CultureInfo.InvariantCulture));
        });
}
