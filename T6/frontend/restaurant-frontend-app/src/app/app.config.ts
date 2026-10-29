import { ApplicationConfig, isDevMode, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { tokenInterceptor } from './auth/token.interceptor';
import { groupOrdersMockInterceptor } from './shared/mocks/group-orders/group-orders-mock.interceptor';
import { applyDemoLoginFromUrl } from './shared/mocks/auth/demo-auth';
import { provideDaterangepickerLocale } from 'ngx-daterangepicker-bootstrap';

import { environment } from '../environments/environment';
import { API_BASE_URL } from './shared/config/api-base-url.token';
import { GROUP_ORDER_SYNC_INTERVAL } from './shared/config/group-order-sync.token';

export const appConfig: ApplicationConfig = {
  providers: [
    // DEV-ONLY: ?demo in any URL — mock login as the Owner of the demo reservation (homework, levels 1–2).
    provideAppInitializer(() => {
      if (isDevMode()) applyDemoLoginFromUrl();
    }),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withXhr(),
      // DEV-ONLY: mock group pre-order for demo-reservation (homework). Not registered in production.
      withInterceptors([tokenInterceptor, ...(isDevMode() ? [groupOrdersMockInterceptor] : [])]),
    ),
    provideDaterangepickerLocale({
      separator: ' - ',
      applyLabel: 'Apply',
      cancelLabel: 'Cancel',
      format: 'MMM D, YYYY',
    }),
    { provide: API_BASE_URL, useValue: environment.backendUrl },
    // US20: the owner sees guests' changes within 5 seconds.
    { provide: GROUP_ORDER_SYNC_INTERVAL, useValue: 5_000 },
  ],
};
