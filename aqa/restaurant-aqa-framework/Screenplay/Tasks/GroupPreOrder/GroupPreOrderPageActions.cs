using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The organizer asks the open group pre-order page for the latest carts.</summary>
public sealed class RefreshTheGroupPreOrderPage : ITask
{
    public Task PerformAsAsync(Actor actor) => new GroupPreOrderPage(actor.LookingAtThePage()).RefreshAsync();
}

/// <summary>The organizer sends the pre-order from the open group page and confirms it in the dialog.</summary>
public sealed class SendThePreOrderFromThePage : ITask
{
    public Task PerformAsAsync(Actor actor) => new GroupPreOrderPage(actor.LookingAtThePage()).SendToTheRestaurantAsync();
}

/// <summary>The organizer removes a guest on the open group page and confirms it in the dialog.</summary>
public sealed class RemoveAGuestOnThePage(string guest) : ITask
{
    public Task PerformAsAsync(Actor actor) => new GroupPreOrderPage(actor.LookingAtThePage()).RemoveGuestAsync(guest);
}
