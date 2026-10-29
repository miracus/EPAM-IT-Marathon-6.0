using APIHandler.Entities;
using System;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

public class GroupReservationStatusResponse
{
    [JsonPropertyName("currentParticipants")]
    public int CurrentParticipants { get; set; }

    [JsonPropertyName("maxCapacity")]
    public int MaxCapacity { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter<GroupReservationStatus>))]
    public GroupReservationStatus Status { get; set; }

    /// <summary>US16 AC5: the guest session passed in ?guestSessionId= was removed by the owner.</summary>
    [JsonPropertyName("removed")]
    public bool Removed { get; set; }

    [JsonPropertyName("cutoffAt")]
    public DateTime? CutoffAt { get; set; }

    [JsonPropertyName("secondsUntilCutoff")]
    public long SecondsUntilCutoff { get; set; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }
}
