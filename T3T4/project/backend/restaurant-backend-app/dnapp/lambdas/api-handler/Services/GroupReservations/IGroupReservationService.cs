using APIHandler.Entities;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestReservation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public interface IGroupReservationService
{
    Task<GroupReservation> GetGroupReservationAsync(string groupReservationId);
    Task<GroupReservation> CreateGroupReservationAsync(string reservationId);
    Task UpdateGroupReservationStatusAsync(string groupReservationId, GroupReservationStatus status);
    Task DeleteGroupReservationAsync(string groupReservationId);
    /// <param name="requesterId">Cognito id of the caller, if any. The reservation owner skips the guest-only checks.</param>
    Task<GetGroupReservationResponse> GetGroupReservationByTokenAsync(string token, string? requesterId = null);
    Task CreateGroupReservationIfDoesNotExistAsync(string userId, string reservationId);
    Task CloseGroupReservationAsync(string reservationId);
    /// <param name="requesterId">Cognito id of the caller. The reservation owner gets the token even after the group was submitted.</param>
    Task<GetGroupInviteTokenResponse> GetGroupReservationInviteTokenAsync(string reservationId, string? requesterId = null);
}
