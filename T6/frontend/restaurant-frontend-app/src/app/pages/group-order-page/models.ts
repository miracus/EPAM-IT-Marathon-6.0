import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';

/** Стан першого завантаження екрана. */
export type LoadState = 'loading' | 'ready' | 'not-found' | 'error';

/** Усе, що потрібно екрану після першого завантаження. */
export interface GroupOrderSnapshot {
  invite: GuestInvite;
  ownerSessionId: string;
  participants: Participant[];
  status: GroupReservationStatus;
}

/** Результат фонової синхронізації. */
export interface GroupOrderSync {
  participants: Participant[];
  status: GroupReservationStatus;
}
