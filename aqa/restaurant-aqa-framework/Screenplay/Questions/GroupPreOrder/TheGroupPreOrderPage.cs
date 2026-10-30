using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The group pre-order as the organizer's page shows it: every participant card read at once, so the
/// answer is one consistent picture even though the page refreshes itself in the background. The grand total on
/// the page is what will be sent to the restaurant, i.e. the confirmed carts only.</summary>
public sealed partial class TheGroupPreOrderPage : IQuestion<GroupPreOrderView>
{
    private const string ReadCards = """
        cards => cards.map(card => ({
          title: card.getAttribute('aria-label') ?? '',
          chip: (card.querySelector('.chip.submitted, .chip.pending, .chip.changed')?.textContent ?? '').trim(),
          subtotal: (card.querySelector('.subtotal span:last-child')?.textContent ?? '').trim(),
          items: [...card.querySelectorAll('li.item')].map(item => ({
            name: (item.querySelector('p.name')?.firstChild?.textContent ?? '').trim(),
            quantity: (item.querySelector('.qty')?.textContent ?? '').trim()
          }))
        }))
        """;

    public async Task<GroupPreOrderView> AnsweredByAsync(Actor actor)
    {
        var page = new GroupPreOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();
        await page.ParticipantCards.First.WaitForAsync();

        var cards = await page.ParticipantCards.EvaluateAllAsync<JsonElement>(ReadCards);
        var participants = cards.EnumerateArray().Select(ToSummary).ToList();
        var total = Money(await page.GrandTotal.InnerTextAsync());

        var guests = participants.Where(p => p.IsGuest).Select(p => p.Summary).ToList();
        return new GroupPreOrderView(guests, total, participants.Sum(p => p.Summary.Subtotal), participants.Count);
    }

    /// <summary>"$1,234.50" → 1234.50.</summary>
    public static decimal Money(string text)
    {
        var digits = NotPartOfANumber().Replace(text, "");
        return digits.Length == 0 ? 0 : decimal.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static (bool IsGuest, ParticipantSummary Summary) ToSummary(JsonElement card)
    {
        const string guestPrefix = "Guest: ";
        var title = card.GetProperty("title").GetString() ?? "";
        var isGuest = title.StartsWith(guestPrefix, StringComparison.Ordinal);

        var dishes = card.GetProperty("items").EnumerateArray().Select(item => new DishLine(
            Restaurant.PlainName(item.GetProperty("name").GetString() ?? ""),
            int.Parse(LastNumber().Match(item.GetProperty("quantity").GetString() ?? "0").Value, CultureInfo.InvariantCulture)));

        var cart = (card.GetProperty("chip").GetString() ?? "") switch
        {
            "Submitted" => ParticipantSummary.Confirmed,
            "Still choosing" => ParticipantSummary.StillChoosing,
            "Changed by you" => ParticipantSummary.ChangedByTheOrganizer,
            var other => other,
        };

        return (isGuest, new ParticipantSummary(
            isGuest ? title[guestPrefix.Length..] : Personas.Organizer,
            DishLines.Describe(dishes),
            Money(card.GetProperty("subtotal").GetString() ?? ""),
            cart));
    }

    [GeneratedRegex(@"[^0-9.\-]")]
    private static partial Regex NotPartOfANumber();

    [GeneratedRegex(@"\d+(?!.*\d)")]
    private static partial Regex LastNumber();
}
