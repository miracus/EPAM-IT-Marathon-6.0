using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The dishes on the waiter's pre-order page, each with its "Quantity: N".</summary>
public sealed partial class TheDishesOnTheWaiterPage : IQuestion<IReadOnlyList<DishLine>>
{
    public async Task<IReadOnlyList<DishLine>> AnsweredByAsync(Actor actor)
    {
        var page = new WaiterPreOrderPage(actor.LookingAtThePage());
        await page.DishCards.First.WaitForAsync();

        var cards = await page.DishCards.EvaluateAllAsync<JsonElement>("""
            cards => cards.map(card => ({
              name: (card.querySelector('.dish-name')?.textContent ?? '').trim(),
              quantity: (card.querySelector('.pre-order-quantity')?.textContent ?? '').trim()
            }))
            """);

        return DishLines.Merge(cards.EnumerateArray().Select(card => new DishLine(
            Restaurant.PlainName(card.GetProperty("name").GetString() ?? ""),
            int.Parse(Number().Match(card.GetProperty("quantity").GetString() ?? "0").Value, CultureInfo.InvariantCulture))));
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
