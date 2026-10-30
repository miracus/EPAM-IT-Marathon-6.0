using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>What the organizer's group page says around the carts: whether the group was found, its status badge,
/// whether the pre-order can be sent, the consolidated-order message and the "not submitted yet" warning.</summary>
public sealed class TheGroupPreOrderPageState : IQuestion<GroupPreOrderPageState>
{
    public async Task<GroupPreOrderPageState> AnsweredByAsync(Actor actor)
    {
        var page = new GroupPreOrderPage(actor.LookingAtThePage());
        await page.WaitUntilLoadedAsync();

        if (await page.NotFound.IsVisibleAsync())
            return new GroupPreOrderPageState(NotFound: true, Status: "", CanSend: false, ConsolidatedMessage: "", PendingWarning: "");

        return new GroupPreOrderPageState(
            NotFound: false,
            Status: (await page.StatusBadge.InnerTextAsync()).Trim(),
            CanSend: await page.SubmitButton.CountAsync() > 0 && await page.SubmitButton.IsEnabledAsync(),
            ConsolidatedMessage: await TextIfShown(page.ConsolidatedEmptyMessage),
            PendingWarning: await TextIfShown(page.PendingWarning));
    }

    private static async Task<string> TextIfShown(ILocator locator) =>
        await locator.CountAsync() > 0 ? (await locator.First.InnerTextAsync()).Trim() : "";
}
