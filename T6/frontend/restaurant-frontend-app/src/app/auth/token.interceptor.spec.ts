import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;
  let authService: jasmine.SpyObj<AuthenticationService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });

  describe('group pre-order owner endpoints (/guest/groups/...)', () => {
    const GROUP_URL = 'https://test.api/guest/groups/grp-1/sessions';

    it('adds the Cognito id token, because the owner endpoints are protected', () => {
      authService.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access-token' });

      http.get(GROUP_URL).subscribe();

      const req = httpMock.expectOne(GROUP_URL);
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush({ participants: [], total: 0 });
    });

    it('keeps an explicit guest-session token untouched', () => {
      authService.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access-token' });

      http.get(GROUP_URL, { headers: { Authorization: 'Bearer guest-token' } }).subscribe();

      const req = httpMock.expectOne(GROUP_URL);
      expect(req.request.headers.get('Authorization')).toBe('Bearer guest-token');
      req.flush({ participants: [], total: 0 });
    });

    it('adds the id token to /guest/invites/ so the backend can recognise the owner', () => {
      authService.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access-token' });
      const url = 'https://test.api/guest/invites/tok-1';

      http.get(url).subscribe();

      const req = httpMock.expectOne(url);
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush({});
    });

    it('sends /guest/invites/ without a token for anonymous guests', () => {
      const url = 'https://test.api/guest/invites/tok-1';

      http.get(url).subscribe();

      const req = httpMock.expectOne(url);
      expect(req.request.headers.has('Authorization')).toBeFalse();
      req.flush({});
    });

    it('does not add a token to other guest endpoints', () => {
      authService.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access-token' });
      const url = 'https://test.api/guest/sessions/gs-1/menu';

      http.get(url).subscribe();

      const req = httpMock.expectOne(url);
      expect(req.request.headers.has('Authorization')).toBeFalse();
      req.flush({ dishes: {} });
    });

    it('respects SKIP_ERROR_TOAST for silent background requests', () => {
      http
        .get(GROUP_URL, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
        .subscribe({ error: () => undefined });

      httpMock.expectOne(GROUP_URL).flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

      expect(toastService.error).not.toHaveBeenCalled();
    });
  });
});
