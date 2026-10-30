using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The organizer's group pre-order page (`/reservations/{id}/group-order`): one card per participant
/// (the organizer's own "My Pre-Order" first, then "Guest: name"), the consolidated pre-order of the confirmed carts,
/// the grand total and the send / remove actions with their confirmation dialogs.</summary>
public sealed class GroupPreOrderPage(IPage page) : BasePage(page)
{
    public ILocator Title => Page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order", Exact = true });
    public ILocator StatusBadge => Page.Locator(".status-badge");
    public ILocator Participants => Page.GetByRole(AriaRole.List, new() { Name = "Participants", Exact = true });
    public ILocator ParticipantCards => Participants.Locator("article");
    public ILocator GrandTotal => Page.Locator(".grand-total-value");
    public ILocator RefreshButton => Page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });
    public ILocator SubmitButton => Page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true });
    public ILocator PendingWarning => Page.Locator("p.banner").Filter(new() { HasText = "submitted their cart yet" });
    public ILocator Consolidated => Page.GetByRole(AriaRole.Region, new() { Name = "Consolidated pre-order", Exact = true });
    public ILocator ConsolidatedLines => Consolidated.Locator(".order-line");
    public ILocator ConsolidatedEmptyMessage => Consolidated.Locator(".empty");
    public ILocator NotFound => Page.GetByText("Group reservation not found", new() { Exact = true });

    private ILocator SubmitDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Submit Pre-Order", Exact = true });
    private ILocator RemoveDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Remove Guest", Exact = true });

    public ILocator GuestCard(string name) => Page.GetByRole(AriaRole.Article, new() { Name = $"Guest: {name}", Exact = true });

    public Task NavigateAsync(string reservationId) => NavigateToAsync($"/reservations/{Uri.EscapeDataString(reservationId)}/group-order");

    /// <summary>Waits until the page has either shown the group (its grand total) or said it was not found.</summary>
    public Task WaitUntilLoadedAsync() =>
        GrandTotal.Or(NotFound).First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

    /// <summary>Pulls fresh carts and waits until the page shows them.</summary>
    public async Task RefreshAsync()
    {
        await Page.RunAndWaitForResponseAsync(
            () => RefreshButton.ClickAsync(),
            response => response.Url.Contains("/sessions", StringComparison.Ordinal)
                && string.Equals(response.Request.Method, "GET", StringComparison.Ordinal));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true, Disabled = false }).WaitForAsync();
    }

    public async Task SendToTheRestaurantAsync()
    {
        await SubmitButton.ClickAsync();
        await SubmitDialog.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
        await StatusBadge.Filter(new() { HasText = "Sent to Restaurant" }).WaitForAsync();
    }

    public async Task RemoveGuestAsync(string name)
    {
        await GuestCard(name).GetByRole(AriaRole.Button, new() { Name = $"Remove {name}", Exact = true }).ClickAsync();
        await RemoveDialog.GetByRole(AriaRole.Button, new() { Name = "Remove Guest", Exact = true }).ClickAsync();
        await GuestCard(name).WaitForAsync(new() { State = WaitForSelectorState.Detached });
    }
}
