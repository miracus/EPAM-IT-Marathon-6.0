using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The consolidated pre-order on the organizer's page ("Caesar Salad × 3" lines): what goes, or went, to
/// the restaurant.</summary>
public sealed partial class TheConsolidatedPreOrderOnThePage : IQuestion<IReadOnlyList<DishLine>>
{
    public async Task<IReadOnlyList<DishLine>> AnsweredByAsync(Actor actor)
    {
        var page = new GroupPreOrderPage(actor.LookingAtThePage());
        await page.ConsolidatedLines.First.WaitForAsync();

        var texts = await page.ConsolidatedLines.EvaluateAllAsync<JsonElement>(
            "lines => lines.map(line => (line.querySelector('span')?.textContent ?? '').trim())");

        return DishLines.Merge(texts.EnumerateArray().Select(text => Line().Match(text.GetString() ?? "")).Select(match =>
            new DishLine(Restaurant.PlainName(match.Groups["dish"].Value), int.Parse(match.Groups["quantity"].Value, CultureInfo.InvariantCulture))));
    }

    [GeneratedRegex(@"^(?<dish>.+?)\s*×\s*(?<quantity>\d+)$")]
    private static partial Regex Line();
}
