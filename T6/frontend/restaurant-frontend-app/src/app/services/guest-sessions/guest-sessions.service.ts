import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpContext, HttpHeaders } from '@angular/common/http';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { GuestPaymentShare, GuestSessionInfo } from '@app/models/group-orders/guest-session.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { DishDetailsRequest } from '@app/models/group-orders/group-actions.model';

export interface CreateGuestSessionRequest {
    groupReservationId: string;
    guestName: string;
}

export interface CreateGuestSessionResponse {
    SessionId: string;
    SessionToken: string;
}

export interface GuestMenuDish {
    id: string;
    name: string;
    description: string;
    price: number;
    weight?: string;
    calories?: string;
    proteins?: string;
    fats?: string;
    carbohydrates?: string;
    vitamins?: string;
    imageUrl?: string | null;
    state: string;        // "Available" | "On Stop"
    selectable: boolean;  // true/false
}

export type GuestMenuCategoryKey = string;

export interface GuestMenuResponse {
    dishes: Record<GuestMenuCategoryKey, GuestMenuDish[]>;
}

@Injectable({ providedIn: 'root' })
export class GuestSessionsService {
    private readonly baseUrl = inject(API_BASE_URL);
    private readonly http = inject(HttpClient);

    createSession(body: CreateGuestSessionRequest) {
        return this.http.post<CreateGuestSessionResponse>(`${this.baseUrl}/guest/sessions`, body);
    }

    getMenu(guestSessionId: string, sessionToken: string) {
        return this.http.get<GuestMenuResponse>(
            `${this.baseUrl}/guest/sessions/${guestSessionId}/menu`,
            { headers: this.authHeaders(sessionToken) }
        );
    }

    addDish(guestSessionId: string, sessionToken: string, dishId: string, quantity: number) {
        return this.http.post(
            `${this.baseUrl}/guest/sessions/${guestSessionId}/dishes`,
            { dishId, quantity },
            { headers: this.authHeaders(sessionToken) }
        );
    }

    removeDish(guestSessionId: string, sessionToken: string, dishId: string, quantity: number) {
        return this.http.request(
            'delete',
            `${this.baseUrl}/guest/sessions/${guestSessionId}/dishes`,
            { body: { dishId, quantity }, headers: this.authHeaders(sessionToken) }
        );
    }

    submit(guestSessionId: string, sessionToken: string) {
        return this.http.post(
            `${this.baseUrl}/guest/sessions/${guestSessionId}/submit`,
            {},
            { headers: this.authHeaders(sessionToken) }
        );
    }

    // ---- Group pre-order extensions (US13–US21), guest side

    /** The guest's cart, notices, cut-off and payment share. Background call: no error toast. */
    getSession(guestSessionId: string, sessionToken: string) {
        return this.http.get<GuestSessionInfo>(`${this.baseUrl}/guest/sessions/${guestSessionId}`, {
            headers: this.authHeaders(sessionToken),
            context: new HttpContext().set(SKIP_ERROR_TOAST, true),
        });
    }

    /** US16 AC5: tells a guest whose session is gone that the organizer removed them. */
    getGroupStatus(groupReservationId: string, guestSessionId: string) {
        return this.http.get<GroupStatusResponse>(`${this.baseUrl}/guest/groups/${groupReservationId}/status`, {
            params: { guestSessionId },
            context: new HttpContext().set(SKIP_ERROR_TOAST, true),
        });
    }

    /** US18/US19: note and serving time of a dish. */
    updateDish(guestSessionId: string, sessionToken: string, dishId: string, body: DishDetailsRequest) {
        return this.http.put(`${this.baseUrl}/guest/sessions/${guestSessionId}/dishes/${encodeURIComponent(dishId)}`, body, {
            headers: this.authHeaders(sessionToken),
        });
    }

    /** US18: allergies or dietary needs. */
    updateAllergies(guestSessionId: string, sessionToken: string, allergies: string) {
        return this.http.put(`${this.baseUrl}/guest/sessions/${guestSessionId}/allergies`, { allergies }, {
            headers: this.authHeaders(sessionToken),
        });
    }

    /** US16: "OK, got it". */
    acknowledgeNotices(guestSessionId: string, sessionToken: string) {
        return this.http.post(`${this.baseUrl}/guest/sessions/${guestSessionId}/notices/ack`, {}, {
            headers: this.authHeaders(sessionToken),
        });
    }

    /** US16 AC3 "Pick another dish": edit a submitted cart again. */
    reopen(guestSessionId: string, sessionToken: string) {
        return this.http.post(`${this.baseUrl}/guest/sessions/${guestSessionId}/reopen`, {}, {
            headers: this.authHeaders(sessionToken),
        });
    }

    /** US17: simulated payment of the guest's share. */
    pay(guestSessionId: string, sessionToken: string) {
        return this.http.post<GuestPaymentShare>(`${this.baseUrl}/guest/sessions/${guestSessionId}/pay`, {}, {
            headers: this.authHeaders(sessionToken),
        });
    }

    private authHeaders(token: string) {
        return new HttpHeaders({ Authorization: `Bearer ${token}` });
    }
}