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
import { GroupInvite } from '@app/models/group-orders/group-invite.model';
import { PaymentMode, PaymentView } from '@app/models/group-orders/group-payment.model';
import {
  DishDetailsRequest,
  RebookResponse,
  RemindersResponse,
  RestoreDishRequest,
} from '@app/models/group-orders/group-actions.model';

/** HTTP calls of the Owner group pre-order screen (contract: homework-backend.md). */
@Injectable({ providedIn: 'root' })
export class GroupOrdersService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);

  /** reservationId → invite-token → invite data (GroupReservationId, address, time, MaxGuestCount) + the token. */
  getGroupInvite$(reservationId: string): Observable<GroupInvite> {
    return this.http
      .get<GroupInviteToken>(`${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/invite-token`)
      .pipe(
        switchMap(({ token }) =>
          this.http
            .get<GuestInvite>(`${this.baseUrl}/guest/invites/${encodeURIComponent(token)}`)
            .pipe(map((invite) => ({ ...invite, inviteToken: token }))),
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

  // ---- Group pre-order extensions (US13–US21). Every call returns fresh server data.

  /** US15: remove one dish from a guest's cart; the guest gets a notice (US16). */
  removeDish$(groupReservationId: string, guestSessionId: string, dishId: string): Observable<GroupSessionsResponse> {
    return this.http.delete<GroupSessionsResponse>(`${this.sessionUrl(groupReservationId, guestSessionId)}/dishes/${encodeURIComponent(dishId)}`);
  }

  /** US15 AC2: Undo — put a removed dish back with the same quantity, note and serving time. */
  restoreDish$(
    groupReservationId: string,
    guestSessionId: string,
    dishId: string,
    body: RestoreDishRequest,
  ): Observable<GroupSessionsResponse> {
    return this.http.post<GroupSessionsResponse>(
      `${this.sessionUrl(groupReservationId, guestSessionId)}/dishes/${encodeURIComponent(dishId)}/restore`,
      body,
    );
  }

  /** US15 AC4: "Ask to change order" — the guest's cart opens for editing again. */
  requestChange$(groupReservationId: string, guestSessionId: string): Observable<GroupSessionsResponse> {
    return this.http.post<GroupSessionsResponse>(`${this.sessionUrl(groupReservationId, guestSessionId)}/change-request`, {});
  }

  /** US14: remind the given guests, or everyone who has not chosen yet (or not paid, at the payment step). */
  remind$(groupReservationId: string, guestSessionIds: string[] = []): Observable<RemindersResponse> {
    return this.http.post<RemindersResponse>(`${this.groupUrl(groupReservationId)}/reminders`, { guestSessionIds });
  }

  /** US14 AC3: auto-remind on/off. */
  updateSettings$(groupReservationId: string, autoRemind: boolean): Observable<GroupSessionsResponse> {
    return this.http.put<GroupSessionsResponse>(`${this.groupUrl(groupReservationId)}/settings`, { autoRemind });
  }

  /** US18/US19: note and serving time of a dish in the owner's own pre-order. */
  updateMyDish$(groupReservationId: string, dishId: string, body: DishDetailsRequest): Observable<GroupSessionsResponse> {
    return this.http.put<GroupSessionsResponse>(`${this.groupUrl(groupReservationId)}/my-dishes/${encodeURIComponent(dishId)}`, body);
  }

  /** US18: the owner's own allergies. */
  updateMyAllergies$(groupReservationId: string, allergies: string): Observable<GroupSessionsResponse> {
    return this.http.put<GroupSessionsResponse>(`${this.groupUrl(groupReservationId)}/my-allergies`, { allergies });
  }

  /** US17: shares and payment status (simulated payments). */
  getPayment$(groupReservationId: string): Observable<PaymentView> {
    return this.http.get<PaymentView>(`${this.groupUrl(groupReservationId)}/payment`);
  }

  setPaymentMode$(groupReservationId: string, mode: PaymentMode): Observable<PaymentView> {
    return this.http.put<PaymentView>(`${this.groupUrl(groupReservationId)}/payment`, { mode });
  }

  sendPaymentLinks$(groupReservationId: string): Observable<PaymentView> {
    return this.http.post<PaymentView>(`${this.groupUrl(groupReservationId)}/payment/links`, {});
  }

  markPaid$(groupReservationId: string, guestSessionId: string): Observable<PaymentView> {
    return this.http.post<PaymentView>(
      `${this.groupUrl(groupReservationId)}/payment/${encodeURIComponent(guestSessionId)}/paid`,
      {},
    );
  }

  /** US21: link a new reservation to the previous one; its guests see the new invite. */
  rebook$(reservationId: string, previousReservationId: string): Observable<RebookResponse> {
    return this.http.post<RebookResponse>(
      `${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/rebook`,
      { previousReservationId },
    );
  }

  private groupUrl(groupReservationId: string): string {
    return `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}`;
  }

  private sessionUrl(groupReservationId: string, guestSessionId: string): string {
    return `${this.groupUrl(groupReservationId)}/sessions/${encodeURIComponent(guestSessionId)}`;
  }

  /** silent: true — background request, errors are not shown as a toast. */
  private context({ silent = false }: GroupRequestOptions): HttpContext {
    return new HttpContext().set(SKIP_ERROR_TOAST, silent);
  }
}
