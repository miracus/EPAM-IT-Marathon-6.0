import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';

/** State of the initial screen load. */
export type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

/** Everything the screen needs after the initial load. */
export interface GroupOrderSnapshot {
  invite: GuestInvite;
  ownerSessionId: string;
  participants: Participant[];
  status: GroupReservationStatus;
}

/** Result of a background sync. */
export interface GroupOrderSync {
  participants: Participant[];
  status: GroupReservationStatus;
}
