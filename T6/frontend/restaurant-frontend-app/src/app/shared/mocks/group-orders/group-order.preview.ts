import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { DEMO_INVITE, DEMO_OWNER_SESSION_ID, GroupOrdersMockStore } from './group-orders.mock-store';

/**
 * Static data for LEVEL 1 (layout): import it into the page instead of HTTP.
 * The same data the mock backend returns, so at level 2 the screen looks the same.
 */
export const GROUP_ORDER_PREVIEW: {
  invite: GuestInvite;
  sessions: GroupSessionsResponse;
  ownerSessionId: string;
} = {
  invite: DEMO_INVITE,
  sessions: new GroupOrdersMockStore().snapshot(),
  ownerSessionId: DEMO_OWNER_SESSION_ID,
};
