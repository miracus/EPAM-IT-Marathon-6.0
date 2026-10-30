using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>Whether the organizer's group is still collecting carts ("Open") or was sent ("Submitted").</summary>
public sealed class TheGroupStatus : IQuestion<string>
{
    public async Task<string> AnsweredByAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var response = await new GroupPreOrderApi(actor.UsingTheApi()).GetStatusAsync(booking.GroupReservationId);
        if (!response.Ok)
            throw new InvalidOperationException($"The group status is not available: {response.Status} {await response.ReadErrorMessageAsync()}");

        return (await response.ReadAsAsync<GroupStatusResponse>()).Status;
    }
}
