// Інтеграція з НАШИМ бекендом (.NET, T3T4): справжні GroupOrdersService + tokenInterceptor,
// відповіді — точно у форматі, який віддає API Gateway (PascalCase у /guest/invites, camelCase у решті,
// помилки { error } / { message, errorCode }). У мережу тест не ходить — HttpTestingController.
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { tokenInterceptor } from '@app/auth/token.interceptor';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupOrderPageComponent } from './group-order-page.component';

const API = 'https://api.test';
const RES = '0526c09c-c42c-407c-a994-86ee3a6c88c9';
const GROUP = '53a1f356-704e-46aa-9b38-89e5ad721297';

/** GET /guest/invites/{token} — CreateSuccessResponse без naming policy → PascalCase, DateTime без «Z». */
const INVITE_BODY = {
  GroupReservationId: GROUP,
  LocationId: 'loc-1',
  RestaurantName: 'Green & Tasty',
  LocationAddress: 'E2E street 1027103859',
  StartTime: '2026-10-29T12:00:00',
  EndTime: '2026-10-29T13:30:00',
  MaxGuestCount: 4,
  TableNumber: 1,
  InviterName: 'Oleh',
};

const dish = (id: string, name: string, price: number) => ({
  id, name, description: `${name} (e2e)`, dishType: 'Appetizer', price, weight: '300 g', calories: '200',
  proteins: '10', fats: '5', carbohydrates: '20', vitamins: 'A', imageUrl: null, state: 'Available',
});
const SALAD = dish('d-salad', 'Spring Salad', 19);
const SOUP = dish('d-soup', 'Tom Yum', 22);

/** GET /guest/groups/{id}/sessions — CreateJsonResponse (camelCase) + наші розширення US12. */
function sessionsBody(annaSubmitted = true) {
  return {
    participants: [
      // owner-сесію бекенд створює порожньою й непідтвердженою
      { id: 'gs-owner', type: 'Owner', name: 'Oleh', items: [], total: 0, isSubmitted: false },
      { id: 'gs-anna', type: 'Guest', name: 'Anna', items: [{ dish: SALAD, quantity: 2 }, { dish: SOUP, quantity: 1 }], total: 60, isSubmitted: annaSubmitted },
      { id: 'gs-olena', type: 'Guest', name: 'Olena', items: [{ dish: SOUP, quantity: 1 }], total: 22, isSubmitted: false },
    ],
    total: 82,
    summaryByDish: [],
    hasUnavailableDishes: false,
    groupStatus: 'Open',
  };
}

describe('Інтеграція з нашим бекендом (формати .NET API)', () => {
  let http: HttpTestingController;
  let toast: jasmine.SpyObj<ToastService>;
  let element: HTMLElement;
  let fixture: ReturnType<typeof TestBed.createComponent<GroupOrderPageComponent>>;

  function flushInitialLoad(annaSubmitted = true): void {
    http.expectOne(`${API}/reservations/${RES}/invite-token`).flush({ token: 'tok-1' });
    http.expectOne(`${API}/guest/invites/tok-1`).flush(INVITE_BODY);
    http.expectOne(`${API}/reservations/${RES}/guest-session-id`).flush({ guestSessionId: 'gs-owner' });
    http.expectOne(`${API}/guest/groups/${GROUP}/sessions`).flush(sessionsBody(annaSubmitted));
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 4, status: 'Open' });
    fixture.detectChanges();
  }

  function button(text: string, root: ParentNode = element): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  }

  beforeEach(() => {
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    const auth = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', ['getTokens']);
    auth.getTokens.and.returnValue({ idToken: 'cognito-id-token', accessToken: 'access' });

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

  it('endpoint-и Owner (Cognito) отримують id token, публічне запрошення — теж не ламається', () => {
    const tokenReq = http.expectOne(`${API}/reservations/${RES}/invite-token`);
    expect(tokenReq.request.headers.get('Authorization')).toBe('Bearer cognito-id-token');
    tokenReq.flush({ token: 'tok-1' });
    http.expectOne(`${API}/guest/invites/tok-1`).flush(INVITE_BODY);
    http.expectOne(`${API}/reservations/${RES}/guest-session-id`).flush({ guestSessionId: 'gs-owner' });

    const sessionsReq = http.expectOne(`${API}/guest/groups/${GROUP}/sessions`);
    expect(sessionsReq.request.headers.get('Authorization')).toBe('Bearer cognito-id-token');
    sessionsReq.flush(sessionsBody());
    http.expectOne(`${API}/guest/groups/${GROUP}/status`).flush({ currentParticipants: 3, maxCapacity: 4, status: 'Open' });
  });

  it('рендерить відповідь бекенду: шапка з PascalCase, My Pre-Order, суми лише з підтверджених кошиків', () => {
    flushInitialLoad();
    const text = element.textContent ?? '';

    expect(text).toContain('E2E street 1027103859');
    expect(text).toContain('Oct 29, 2026');
    expect(text).toContain('3 of 4 guests');
    expect(element.querySelectorAll('app-participant-card').length).toBe(3);
    expect(text).toContain('My Pre-Order');
    expect(text).toContain('Guest: Anna');
    // підтверджена лише Anna: 2 × 19 + 22 = 60, а не 82 з API
    expect(text).toContain('$60.00');
    expect(text).not.toContain('$82.00');
    // owner-сесія і Olena не підтверджені
    expect(text).toMatch(/2 participants haven.t submitted/);
    // imageUrl: null → перша літера замість фото
    expect(element.querySelector('app-participant-card .initial')?.textContent?.trim()).toBe('S');
  });

  it('submit: POST з { reservationId }, після 200 — Sent to Restaurant', () => {
    flushInitialLoad();
    button('Submit Pre-Order')!.click();
    fixture.detectChanges();
    button('Submit', element.querySelector('[role="dialog"]')!)!.click();

    const req = http.expectOne(`${API}/group-reservations/${GROUP}/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reservationId: RES });
    expect(req.request.headers.get('Authorization')).toBe('Bearer cognito-id-token');
    req.flush([{ dishId: 'd-salad', quantity: 2 }, { dishId: 'd-soup', quantity: 1 }]);
    fixture.detectChanges();

    expect(element.textContent).toContain('Sent to Restaurant');
    expect(toast.success).toHaveBeenCalledWith('Pre-order sent to the restaurant');
  });

  it('submit: 400 бекенду { message, errorCode } → toast з текстом API, кнопка знову активна', () => {
    flushInitialLoad();
    button('Submit Pre-Order')!.click();
    fixture.detectChanges();
    button('Submit', element.querySelector('[role="dialog"]')!)!.click();

    http
      .expectOne(`${API}/group-reservations/${GROUP}/submit`)
      .flush({ message: 'Order for reservation was not found.', errorCode: 0 }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(toast.error).toHaveBeenCalledWith('Order for reservation was not found.', 'Error');
    expect(button('Submit Pre-Order')?.disabled).toBeFalse();
    expect(element.querySelector('[role="dialog"]')).toBeNull();
  });

  it('remove guest: DELETE з токеном, 409 бекенду { error } → toast, список не змінюється', () => {
    flushInitialLoad();
    (element.querySelector('button[aria-label="Remove Olena"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    button('Remove Guest', element.querySelector('[role="dialog"]')!)!.click();

    const req = http.expectOne(`${API}/guest/groups/${GROUP}/sessions/gs-olena`);
    expect(req.request.method).toBe('DELETE');
    expect(req.request.headers.get('Authorization')).toBe('Bearer cognito-id-token');
    req.flush({ error: 'Group reservation was submitted.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(toast.error).toHaveBeenCalledWith('Group reservation was submitted.', 'Conflict');
    expect(element.querySelectorAll('app-participant-card').length).toBe(3);
  });

  it('запрошення недоступне (400 від /guest/invites, напр. група заповнена) → екран помилки з Try again', () => {
    http.expectOne(`${API}/reservations/${RES}/invite-token`).flush({ token: 'tok-1' });
    http
      .expectOne(`${API}/guest/invites/tok-1`)
      .flush({ message: 'Guest limit exceeded for this reservation.', errorCode: 2 }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(button('Try again')).toBeDefined();
  });
});
