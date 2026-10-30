using System.Text.Json.Serialization;

namespace Restaurant_Aqa_Framework.Api.Dtos.Orders;

/// <summary>The backend writes <see cref="Status"/> as a number; the converter also accepts the name, so the DTO
/// keeps working if the backend switches to string enums.</summary>
public sealed record GetOrderResponse(
    string OrderId,
    string ReservationId,
    string ReservationAddress,
    string ReservationTime,
    IReadOnlyList<OrderItemResponse> OrderItems,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OrderStatus Status);
