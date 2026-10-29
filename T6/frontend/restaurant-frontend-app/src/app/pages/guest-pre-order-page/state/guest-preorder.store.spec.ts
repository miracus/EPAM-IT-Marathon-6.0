// Guest side of the group pre-order extensions (US13, US16, US17, US18, US19, US21).
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GuestSessionInfo } from '@app/models/group-orders/guest-session.model';
import { GuestPreorderStore } from './guest-preorder.store';

const API = 'https://api.test';
const TOKEN = 'invite-token';
const INVITE = {
  GroupReservationId: 'grp-1', LocationId: 'loc-1', RestaurantName: 'Green & Tasty', LocationAddress: 'Main street 1',
  StartTime: '2026-11-07T19:00:00', EndTime: '2026-11-07T20:30:00', MaxGuestCount: 5, TableNumber: 1, InviterName: 'Oleh',
};

function session(extra: Partial<GuestSessionInfo> = {}): GuestSessionInfo {
  return {
    name: 'Anna',
    items: [{ dish: { id: 'cake', name: 'Berry Cheesecake', price: 9, dishType: 'Dessert' }, quantity: 2, note: 'Candle', serveTime: 'AfterMains' }],
    total: 18,
    isSubmitted: true,
    notices: [],
    allergies: 'Nuts',
    groupStatus: 'Open',
    cutoffAt: '2026-11-07T17:00:00',
    secondsUntilCutoff: 80 * 60,
    isLocked: false,
    ownerName: 'Oleh',
    ...extra,
  };
}

describe('GuestPreorderStore — group pre-order extensions', () => {
  let store: GuestPreorderStore;
  let http: HttpTestingController;

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  }

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionId`, 'anna');
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionToken`, 'secret');

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: API }],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(GuestPreorderStore);

    store.initContext(TOKEN);
    http.expectOne(`${API}/guest/invites/${TOKEN}`).flush(INVITE);
    http.expectOne(`${API}/guest/sessions/anna/menu`).flush({ dishes: {} });
    await settle();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('loads the cart from the server: notes, serving time, allergies, countdown', async () => {
    const req = http.expectOne(`${API}/guest/sessions/anna`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer secret');
    req.flush(session());
    await settle();

    expect(store.status()).toBe('submitted');
    expect(store.cartItems()[0]).toEqual(jasmine.objectContaining({ dishId: 'cake', qty: 2, note: 'Candle', serveTime: 'AfterMains' }));
    expect(store.allergies()).toBe('Nuts');
    expect(store.timeLeft()).toBe('in 1h 20m');
    expect(store.isLocked()).toBeFalse();
  });

  it('US16: the organizer removed a dish — notice with old and new total', async () => {
    http.expectOne(`${API}/guest/sessions/anna`).flush(session({
      notices: [{ id: 'n1', message: 'Oleh removed Tom Yum from your order.', createdAt: '', type: 'DishRemoved', dishName: 'Tom Yum', oldTotal: 40, newTotal: 18, actorName: 'Oleh' }],
    }));
    await settle();

    expect(store.removedNotices().length).toBe(1);
    expect(store.removedNotices()[0].oldTotal).toBe(40);
    expect(store.messageNotices().length).toBe(0);
  });

  it('US16 AC5: a removed guest learns about it from the status endpoint', async () => {
    http.expectOne(`${API}/guest/sessions/anna`).flush({ message: 'Invalid guest session token' }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne(`${API}/guest/groups/grp-1/status?guestSessionId=anna`).flush({ currentParticipants: 2, maxCapacity: 5, status: 'Open', removed: true });
    await settle();

    expect(store.removed()).toBeTrue();
  });

  it('US18: a note is saved on the server, then the cart is re-read', async () => {
    http.expectOne(`${API}/guest/sessions/anna`).flush(session());
    await settle();

    const done = store.setNote('cake', '  Happy birthday  ');
    const put = http.expectOne(`${API}/guest/sessions/anna/dishes/cake`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ note: 'Happy birthday' });
    put.flush({});
    await settle();
    http.expectOne(`${API}/guest/sessions/anna`).flush(session());
    await done;
  });

  it('US17: pays the share (simulated)', async () => {
    http.expectOne(`${API}/guest/sessions/anna`).flush(session({ payment: { mode: 'EachPaysOwn', amount: 18, isPaid: false } }));
    await settle();
    expect(store.payment()?.amount).toBe(18);

    const done = store.pay();
    http.expectOne(`${API}/guest/sessions/anna/pay`).flush({ mode: 'EachPaysOwn', amount: 18, isPaid: true });
    await settle();
    http.expectOne(`${API}/guest/sessions/anna`).flush(session({ payment: { mode: 'EachPaysOwn', amount: 18, isPaid: true } }));
    expect(await done).toBeTrue();
    expect(store.payment()?.isPaid).toBeTrue();
  });

  it('US13 AC4 / US21: locked order with an invite to the new booking', async () => {
    http.expectOne(`${API}/guest/sessions/anna`).flush(session({
      isLocked: true, groupStatus: 'Submitted',
      rebook: { inviteToken: 'next-token', startTime: '2026-11-14T19:00:00', endTime: '2026-11-14T20:30:00' },
    }));
    await settle();

    expect(store.isLocked()).toBeTrue();
    expect(store.rebook()?.inviteToken).toBe('next-token');
  });
});
