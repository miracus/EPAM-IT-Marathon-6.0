export type GroupReservationStatus = 'Open' | 'Submitted' | 'Closed';

/** GET /guest/groups/{groupReservationId}/status */
export interface GroupStatusResponse {
  currentParticipants: number;
  maxCapacity: number;
  status: GroupReservationStatus;
  /** US16: true when the guest session passed in ?guestSessionId= was removed by the owner. */
  removed?: boolean;
  cutoffAt?: string | null;
  secondsUntilCutoff?: number;
  isLocked?: boolean;
}
