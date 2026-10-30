using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Ui.Components;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The waiter's reservations (`/waiter-reservations`); with no filter chosen it lists every reservation
/// assigned to the waiter.</summary>
public sealed class WaiterReservationsPage(IPage page) : BasePage(page)
{
    private ILocator ReservationCards => Page.Locator("li.card");

    public Task NavigateAsync() => NavigateToAsync("/waiter-reservations");

    public ReservationCardComponent ReservationByAddress(string address) =>
        new(ReservationCards.Filter(new() { HasText = address }));
}
