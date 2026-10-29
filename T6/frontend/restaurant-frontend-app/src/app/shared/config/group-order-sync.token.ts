import { InjectionToken } from '@angular/core';

/**
 * How often the Owner group pre-order screen pulls fresh carts while the group is Open.
 * The homework (level 3) asks for 15 s; the app provides 5 s for US20 "changes within 5 seconds".
 */
export const GROUP_ORDER_SYNC_INTERVAL = new InjectionToken<number>('GROUP_ORDER_SYNC_INTERVAL', {
  providedIn: 'root',
  factory: () => 15_000,
});
