import { Participant } from './participant.model';
import { GroupReservationStatus } from './group-status.model';
import { PaymentMode } from './group-payment.model';

/** GET /guest/groups/{groupReservationId}/sessions */
export interface GroupSessionsResponse {
  participants: Participant[];
  /** Sum of ALL participants, including those who have not submitted their cart yet. */
  total: number;
  groupStatus?: GroupReservationStatus;
  /** US13: when changes close (same time format as the reservation start). */
  cutoffAt?: string | null;
  /** US13: seconds left until the cut-off, computed on the server. */
  secondsUntilCutoff?: number;
  /** US13: after submit or after the cut-off nothing can be changed. */
  isLocked?: boolean;
  submittedAt?: string | null;
  submittedAutomatically?: boolean;
  /** US14: automatic reminder 30 minutes before the cut-off. */
  autoRemind?: boolean;
  /** US17: chosen payment mode. */
  paymentMode?: PaymentMode | null;
  maxGuests?: number;
  /** US21: Reserved / Cancelled / InProgress / Completed. */
  reservationStatus?: string;
}
