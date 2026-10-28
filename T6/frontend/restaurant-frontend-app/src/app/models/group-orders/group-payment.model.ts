import { ParticipantType } from './participant.model';

/** US17: how the group pays. */
export type PaymentMode = 'EachPaysOwn' | 'OwnerPaysAll' | 'SplitEqually';

export interface PaymentParticipant {
  id: string;
  type: ParticipantType;
  name: string;
  ownDishesTotal: number;
  amount: number;
  isPaid: boolean;
  paidAt?: string | null;
  linkSentAt?: string | null;
}

/** GET/PUT /guest/groups/{groupReservationId}/payment — "Paid $X · Pending $Y", X + Y = Grand Total. */
export interface PaymentView {
  mode: PaymentMode | null;
  participants: PaymentParticipant[];
  grandTotal: number;
  paidTotal: number;
  pendingTotal: number;
}
