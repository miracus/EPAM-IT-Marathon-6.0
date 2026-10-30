namespace Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

/// <summary>POST /guest/sessions: a guest joins a group under the name they typed on the invitation page.</summary>
public sealed record CreateGuestSessionRequest(string GroupReservationId, string GuestName);

/// <summary>POST /guest/sessions/{guestSessionId}/dishes: a guest puts a dish in their own cart.</summary>
public sealed record AddGuestDishRequest(string DishId, int Quantity);

/// <summary>POST /group-reservations/{groupReservationId}/submit: the organizer sends the group pre-order.</summary>
public sealed record SubmitGroupPreOrderRequest(string ReservationId);

/// <summary>POST and DELETE /waiters: puts a registered user on (or takes them off) a location's waiter list.</summary>
public sealed record WaiterListRequest(string Email, string LocationId);
