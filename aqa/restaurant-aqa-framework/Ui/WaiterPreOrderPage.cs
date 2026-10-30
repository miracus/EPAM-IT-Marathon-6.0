using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The waiter's view of one reservation's pre-order (`/waiter-pre-order`): a dish card per line, each
/// with "Quantity: N" and confirm / cancel buttons.</summary>
public sealed class WaiterPreOrderPage(IPage page) : BasePage(page)
{
    public ILocator Heading => Page.GetByRole(AriaRole.Heading).Filter(new() { HasText = "Pre-order of" });
    public ILocator DishCards => Page.Locator(".pre-order-dish");
}
