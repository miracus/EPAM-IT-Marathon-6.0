using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui.Components;

/// <summary>`app-card-reservation` (`li.card`) on `/reservations` and `/waiter-reservations` — the
/// action buttons rendered depend on actor role and reservation status; only the Customer-facing
/// subset needed by the sample flows is modeled here.</summary>
public sealed class ReservationCardComponent(ILocator root)
{
    public ILocator StatusBadge => root.Locator(".status-badge");
    public ILocator Details => root.Locator(".details-line .details");
    public ILocator CancelButton => root.Locator("button.btn-cancel");
    public ILocator EditButton => root.Locator("button.btn-edit.btn-postpone");
    public ILocator PreOrderButton => root.Locator("button.btn-preorder");
    public ILocator LeaveFeedbackButton => root.Locator("button.btn-leave-feedback");
    public ILocator UpdateFeedbackButton => root.Locator("button.btn-update-feedback");

    /// <summary>Customer, a reservation for two or more guests: opens the organizer's group pre-order page.</summary>
    public ILocator GroupPreOrderButton => root.GetByRole(AriaRole.Button, new() { Name = "Group Pre-Order", Exact = true });

    /// <summary>Waiter: opens the pre-order of the reservation (shown once it has dishes).</summary>
    public ILocator WaiterPreOrderButton => root.GetByRole(AriaRole.Button, new() { Name = "Pre-order", Exact = true });

    public Task CancelAsync() => CancelButton.ClickAsync();
    public Task PreOrderAsync() => PreOrderButton.ClickAsync();
    public Task LeaveFeedbackAsync() => LeaveFeedbackButton.ClickAsync();
}
