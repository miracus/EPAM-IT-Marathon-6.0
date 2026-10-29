// Group pre-order extensions US13–US21 on the Owner screen, against the real service and HTTP formats.
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { tokenInterceptor } from '@app/auth/token.interceptor';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const API = 'https://api.test';
const RES = 'res-1';
const GROUP = 'grp-1';

const INVITE = {
  GroupReservationId: GROUP,
  LocationId: 'loc-1',
  RestaurantName: 'Green & Tasty',
  LocationAddress: 'Main street 1',
  StartTime: '2026-11-07T19:00:00',
  EndTime: '2026-11-07T20:30:00',
  MaxGuestCount: 5,
  TableNumber: 1,
  InviterName: 'Oleh',
};

const SALAD = { id: 'salad', name: 'Spring Salad', price: 19, dishType: 'Appetizer' };
const SOUP = { id: 'soup', name: 'Tom Yum', price: 22, dishType: 'Main course' };

function snapshot(extra: Partial<GroupSessionsResponse> = {}): GroupSessionsResponse {
  return {
    participants: [
      { id: 'own', type: 'Owner', name: 'Oleh', items: [{ dish: SALAD, quantity: 1, serveTime: 'OnArrival' }], total: 19, isSubmitted: true },
      {
        id: 'anna', type: 'Guest', name: 'Anna', total: 41, isSubmitted: true, allergies: 'Nuts',
        items: [
          { dish: SALAD, quantity: 1, note: 'No onion', serveTime: 'AfterMains' },
          { dish: SOUP, quantity: 1, serveTime: 'OnArrival' },
        ],
      },
      { id: 'iryna', type: 'Guest', name: 'Iryna', items: [], total: 0, isSubmitted: false },
    ],
    total: 60,
    groupStatus: 'Open',
    cutoffAt: '2026-11-07T17:00:00',
    secondsUntilCutoff: 3 * 3600,
    isLocked: false,
    autoRemind: true,
    maxGuests: 5,
    reservationStatus: 'Reserved',
    ...extra,
  };
}

describe('Group pre-order extensions on the Owner screen (US13–US21)', () => {
  let http: HttpTestingController;
  let toast: jasmine.SpyObj<ToastService>;
  let element: HTMLElement;
  let fixture: ReturnType<typeof TestBed.createComponent<GroupOrderPageComponent>>;

  function load(sessions: GroupSessionsResponse = snapshot(), status = 'Open'): void {
    http.expectOne(`${API}/reservations/${RES}/invite-token`).flush({ token: 'tok-1' });
    http.expectOne(`${API}/guest/invites/tok-1`).flush(INVITE);
    http.expectOne(`${API}/reservations/${RES}/guest-session-id`).flush({ guestSessionId: 'own' });
    http.expectOne(`${API}/guest/groups/${GROUP}/sessions`).flush(sessions);
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 5, status });
    fixture.detectChanges();
  }

  function button(text: string, root: ParentNode = element): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  }

  beforeEach(() => {
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    const auth = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', ['getTokens']);
    auth.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access' });

    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: API },
        { provide: ToastService, useValue: toast },
        { provide: AuthenticationService, useValue: auth },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: RES }) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('US13/US20: cut-off countdown, progress and live indicator', () => {
    load();
    const text = element.textContent ?? '';

    expect(text).toContain('Changes close at 17:00 · in 3h');
    expect(text).toContain('3 of 5 joined · 2 chose dishes');
    expect(text).toContain('Live · Updated at');
  });

  it('US14: "Remind N" reminds guests without dishes and shows who got it', () => {
    load();
    button('Remind 1')!.click();

    const req = http.expectOne(`${API}/guest/groups/${GROUP}/reminders`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ guestSessionIds: [] });
    req.flush({ reminded: [{ id: 'iryna', name: 'Iryna' }], skipped: [] });

    expect(toast.success).toHaveBeenCalledWith('Reminder sent to Iryna');
    // after a reminder the screen refreshes silently
    http.expectOne(`${API}/guest/groups/${GROUP}/sessions`).flush(snapshot());
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 5, status: 'Open' });
  });

  it('US14 AC4: a reminder within 15 minutes is reported as skipped', () => {
    load();
    button('Remind 1')!.click();
    http
      .expectOne(`${API}/guest/groups/${GROUP}/reminders`)
      .flush({ reminded: [], skipped: [{ id: 'iryna', name: 'Iryna', lastRemindedAt: '2026-11-07T15:20:00' }] });

    expect(toast.info).toHaveBeenCalledWith(jasmine.stringMatching(/^Iryna: already reminded at/));
    http.expectOne(`${API}/guest/groups/${GROUP}/sessions`).flush(snapshot());
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 5, status: 'Open' });
  });

  it('US15: trash removes a guest dish without a dialog; Undo puts it back', () => {
    load();
    (element.querySelector('button[aria-label="Remove Tom Yum"]') as HTMLButtonElement).click();

    const del = http.expectOne(`${API}/guest/groups/${GROUP}/sessions/anna/dishes/soup`);
    expect(del.request.method).toBe('DELETE');
    const afterRemove = snapshot();
    afterRemove.participants[1] = { ...afterRemove.participants[1], items: [afterRemove.participants[1].items[0]], total: 19, changedByOwner: true };
    del.flush(afterRemove);
    fixture.detectChanges();

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(element.textContent).toContain('Tom Yum removed — Anna will be notified');
    expect(element.textContent).toContain('Changed by you');

    button('Undo')!.click();
    const restore = http.expectOne(`${API}/guest/groups/${GROUP}/sessions/anna/dishes/soup/restore`);
    expect(restore.request.method).toBe('POST');
    expect(restore.request.body).toEqual({ quantity: 1, note: undefined, serveTime: 'OnArrival' });
    restore.flush(snapshot());
    fixture.detectChanges();

    expect(element.textContent).not.toContain('Anna will be notified');
  });

  it('US15 AC4: "Ask to change order" from the guest menu', () => {
    load();
    (element.querySelector('button[aria-label="More actions for Anna"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    button('Ask to change order')!.click();

    const req = http.expectOne(`${API}/guest/groups/${GROUP}/sessions/anna/change-request`);
    expect(req.request.method).toBe('POST');
    req.flush(snapshot());
    expect(toast.success).toHaveBeenCalledWith('Anna can change the order now', 'Request sent');
  });

  it('US18/US19: notes, allergy chip, blue serve chip; "By dish" groups by serving time', () => {
    load();
    const text = element.textContent ?? '';
    expect(text).toContain('“No onion”');
    expect(element.querySelector('.chip.allergy')?.textContent).toContain('Nuts');
    expect(element.querySelector('.chip.serve')?.textContent).toContain('After mains');

    button('By dish (3)')!.click();
    fixture.detectChanges();

    expect(element.querySelector('app-kitchen-view')).not.toBeNull();
    expect(element.textContent).toContain('Nuts — Anna. Kitchen will be warned.');
    expect(element.textContent).toContain('After mains');
  });

  it('US18: the owner adds a note to their own dish', () => {
    load();
    button('+ Add note')!.click();
    fixture.detectChanges();
    const input = element.querySelector('input[aria-label="Note for Spring Salad"]') as HTMLInputElement;
    input.value = 'Extra lime';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element.textContent).toContain('10/80');
    button('Save')!.click();

    const req = http.expectOne(`${API}/guest/groups/${GROUP}/my-dishes/salad`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ note: 'Extra lime' });
    req.flush(snapshot());
  });

  it('US13 AC3: after the cut-off nothing can be edited', () => {
    load(snapshot({ isLocked: true, secondsUntilCutoff: 0 }));

    expect(element.querySelector('button[aria-label="Remove Tom Yum"]')).toBeNull();
    expect(button('Submit Pre-Order')?.disabled).toBeTrue();
    expect(element.querySelector('svg[aria-label="Locked"]')).not.toBeNull();
  });

  it('US20 AC2: a dish added by a guest is tagged NEW and announced', () => {
    load();
    button('Refresh')!.click();
    const next = snapshot();
    next.participants[2] = { ...next.participants[2], items: [{ dish: SOUP, quantity: 1 }], total: 22 };
    http.expectOne(`${API}/guest/groups/${GROUP}/sessions`).flush(next);
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 5, status: 'Open' });
    fixture.detectChanges();

    expect(toast.info).toHaveBeenCalledWith('Iryna added Tom Yum');
    expect(element.querySelector('.tag-new')?.textContent).toContain('NEW');
  });

  it('US13 AC2 + US17: sent banner, then "Choose how to pay" opens the split', () => {
    load(snapshot({ groupStatus: 'Submitted', isLocked: true, submittedAt: '2026-11-07T16:42:00', submittedAutomatically: true }), 'Submitted');
    expect(element.textContent).toContain('Sent to the restaurant at 16:42 automatically at the cut-off');

    button('Choose how to pay')!.click();
    http.expectOne(`${API}/guest/groups/${GROUP}/payment`).flush({
      mode: null, grandTotal: 60, paidTotal: 0, pendingTotal: 60,
      participants: [
        { id: 'own', type: 'Owner', name: 'Oleh', ownDishesTotal: 19, amount: 19, isPaid: false },
        { id: 'anna', type: 'Guest', name: 'Anna', ownDishesTotal: 41, amount: 41, isPaid: false },
      ],
    });
    fixture.detectChanges();
    expect(element.querySelector('app-payment-panel')).not.toBeNull();

    (element.querySelectorAll('[role="radio"]')[2] as HTMLButtonElement).click();
    const req = http.expectOne(`${API}/guest/groups/${GROUP}/payment`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'SplitEqually' });
    req.flush({
      mode: 'SplitEqually', grandTotal: 60, paidTotal: 0, pendingTotal: 60,
      participants: [
        { id: 'own', type: 'Owner', name: 'Oleh', ownDishesTotal: 19, amount: 30, isPaid: false },
        { id: 'anna', type: 'Guest', name: 'Anna', ownDishesTotal: 41, amount: 30, isPaid: false },
      ],
    });
    fixture.detectChanges();
    expect(element.textContent).toContain('Paid $0.00 · Pending $60.00');
  });

  it('US21: a completed visit can be booked again with the same group', () => {
    load(snapshot({ groupStatus: 'Submitted', isLocked: true, reservationStatus: 'Completed' }), 'Submitted');
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    expect(element.textContent).toContain('3 people · 3 dishes · $60.00');
    button('Book again with same group')!.click();

    expect(navigate).toHaveBeenCalledWith(['/tables'], {
      queryParams: { locationId: 'loc-1', guests: 5, rebookFrom: RES },
    });
  });
});
