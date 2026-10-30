using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Sends the group pre-order to the restaurant as <paramref name="persona"/>. With <c>mustSucceed</c> (the
/// default) a refusal stops the scenario; scenarios that test a refusal read the outcome instead.</summary>
public sealed class SendTheGroupPreOrder(
    string persona = Personas.Organizer,
    bool mustSucceed = true,
    string? reservationId = null,
    string? groupReservationId = null) : ITask
{
    public static SendTheGroupPreOrder Attempt(string persona = Personas.Organizer) => new(persona, mustSucceed: false);

    /// <summary>The organizer names a reservation that does not exist (not the one the group belongs to).</summary>
    public static SendTheGroupPreOrder ForAReservationThatDoesNotExist() =>
        new(mustSucceed: false, reservationId: Guid.NewGuid().ToString());

    /// <summary>The organizer sends a group that was never created.</summary>
    public static SendTheGroupPreOrder ForAGroupThatDoesNotExist() =>
        new(mustSucceed: false, groupReservationId: Guid.NewGuid().ToString());

    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var token = await Accounts.TokenOfAsync(actor, persona);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi()).SubmitAsync(
            groupReservationId ?? booking.GroupReservationId, reservationId ?? booking.ReservationId, token));

        if (mustSucceed && !response.Succeeded)
            throw new InvalidOperationException($"Sending the group pre-order failed with {response.Status}: {response.Body}");
    }
}
