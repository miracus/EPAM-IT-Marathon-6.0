using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Asks for the group pre-order (participants, carts, totals) as <paramref name="persona"/>. The outcome is
/// the scenario's last response, so a scenario can check that someone other than the organizer is refused.</summary>
public sealed class ReviewTheGroupPreOrder(string persona = Personas.Organizer, string? groupReservationId = null) : ITask
{
    /// <summary>The organizer asks for a group that was never created.</summary>
    public static ReviewTheGroupPreOrder OfAGroupThatDoesNotExist() => new(groupReservationId: Guid.NewGuid().ToString());

    public async Task PerformAsAsync(Actor actor)
    {
        var groupId = groupReservationId ?? actor.Recall<GroupBooking>(Personas.Organizer).GroupReservationId;
        var token = await Accounts.TokenOfAsync(actor, persona);

        await actor.TheLastResponse().CaptureAsync(
            await new GroupPreOrderApi(actor.UsingTheApi()).GetSessionsAsync(groupId, token));
    }
}
