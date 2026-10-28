import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GroupInvite } from '@app/models/group-orders/group-invite.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';

/** State of the initial screen load. */
export type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

/** "By guest" cards or the "By dish" kitchen view (US18 AC4, US19 AC4). */
export type GroupOrderView = 'guests' | 'dishes';

/** Everything the screen needs after the initial load. */
export interface GroupOrderSnapshot {
  invite: GroupInvite;
  ownerSessionId: string;
  participants: Participant[];
  status: GroupReservationStatus;
  sessions: GroupSessionsResponse;
}

/** Result of a background sync. */
export interface GroupOrderSync {
  participants: Participant[];
  status: GroupReservationStatus;
  sessions: GroupSessionsResponse;
}

/** Cut-off, lock and settings of the group (US13, US14, US17, US21) from the last snapshot. */
export interface GroupOrderMeta {
  cutoffAt: string | null;
  /** Seconds to the cut-off when the snapshot arrived, and when it arrived (Date.now()). */
  secondsUntilCutoff: number;
  receivedAt: number;
  isLocked: boolean;
  submittedAt: string | null;
  submittedAutomatically: boolean;
  autoRemind: boolean;
  paymentMode: string | null;
  maxGuests: number | null;
  reservationStatus: string | null;
}

/** A line highlighted as "NEW" until the given time (US20 AC2). */
export interface HighlightedLine {
  key: string;
  until: number;
}
