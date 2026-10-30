using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Ui.Components;

namespace Restaurant_Aqa_Framework.Ui;

public sealed class ReservationDashboardPage(IPage page) : BasePage(page)
{
    public HeaderComponent Header { get; } = new(page);
    public CartComponent Cart { get; } = new(page);

    // Each card is rendered by `app-card-reservation` inside the list item, so the card is a descendant, not a child.
    private ILocator ReservationCards => Page.Locator("ul.reservation-list li.card");
    public ILocator EmptyState => Page.Locator(".reservation-empty");

    public Task NavigateAsync() => NavigateToAsync("/reservations");

    public ReservationCardComponent Reservation(int index) => new(ReservationCards.Nth(index));

    public ReservationCardComponent ReservationByAddress(string address) =>
        new(ReservationCards.Filter(new() { HasText = address }));
}
