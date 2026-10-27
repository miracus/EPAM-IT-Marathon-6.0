using APIHandler.Models.Orders;
using System.Collections.Generic;

namespace APIHandler.Models.GuestSessions;

public record GetGuestSessionResponse(
    string Name,
    IEnumerable<OrderItemResponse> Items,
    decimal Total,
    bool IsSubmitted)
{
    /// <summary>Unacknowledged changes the reservation owner made to this guest's order (US16).</summary>
    public IEnumerable<GuestNoticeResponse> Notices { get; init; } = [];
}
