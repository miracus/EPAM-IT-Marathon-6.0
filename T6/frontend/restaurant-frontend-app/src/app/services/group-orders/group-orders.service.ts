import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, map, switchMap } from 'rxjs';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { GroupInviteToken, GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { OwnerSessionResponse } from '@app/models/group-orders/owner-session.model';
import { SubmitGroupPreOrderRequest, SubmittedDish } from '@app/models/group-orders/submit-group-pre-order.model';
import { GroupRequestOptions } from '@app/models/group-orders/group-request-options.model';

/** HTTP calls of the Owner group pre-order screen (contract: homework-backend.md). */
@Injectable({ providedIn: 'root' })
export class GroupOrdersService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);

  /** reservationId → invite-token → invite data (GroupReservationId, address, time, MaxGuestCount). */
  getGroupInvite$(reservationId: string): Observable<GuestInvite> {
    return this.http
      .get<GroupInviteToken>(`${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/invite-token`)
      .pipe(
        switchMap(({ token }) =>
          this.http.get<GuestInvite>(`${this.baseUrl}/guest/invites/${encodeURIComponent(token)}`),
        ),
      );
  }

  getSessions$(groupReservationId: string, options: GroupRequestOptions = {}): Observable<GroupSessionsResponse> {
    return this.http.get<GroupSessionsResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/sessions`,
      { context: this.context(options) },
    );
  }

  getOwnerSessionId$(reservationId: string): Observable<string> {
    return this.http
      .get<OwnerSessionResponse>(`${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/guest-session-id`)
      .pipe(map((response) => response.guestSessionId));
  }

  getStatus$(groupReservationId: string, options: GroupRequestOptions = {}): Observable<GroupStatusResponse> {
    return this.http.get<GroupStatusResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/status`,
      { context: this.context(options) },
    );
  }

  submit$(groupReservationId: string, reservationId: string): Observable<SubmittedDish[]> {
    const body: SubmitGroupPreOrderRequest = { reservationId };
    return this.http.post<SubmittedDish[]>(
      `${this.baseUrl}/group-reservations/${encodeURIComponent(groupReservationId)}/submit`,
      body,
    );
  }

  /** Bonus: the Owner removes a guest; the response is the updated group snapshot. */
  removeSession$(groupReservationId: string, guestSessionId: string): Observable<GroupSessionsResponse> {
    return this.http.delete<GroupSessionsResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/sessions/${encodeURIComponent(guestSessionId)}`,
    );
  }

  /** silent: true — background request, errors are not shown as a toast. */
  private context({ silent = false }: GroupRequestOptions): HttpContext {
    return new HttpContext().set(SKIP_ERROR_TOAST, silent);
  }
}
