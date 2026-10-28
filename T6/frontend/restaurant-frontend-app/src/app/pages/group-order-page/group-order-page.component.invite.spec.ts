// Invite link on the owner screen: the owner can always copy the guest link again.
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupInvite } from '@app/models/group-orders/group-invite.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const INVITE = {
  GroupReservationId: 'grp-1',
  LocationAddress: 'Rustaveli Avenue 48',
  MaxGuestCount: 4,
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
  inviteToken: 'tok-123',
} as GroupInvite;

describe('Owner screen · invite link', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  let toast: jasmine.SpyObj<ToastService>;

  function create(invite: GroupInvite = INVITE, status: GroupStatusResponse['status'] = 'Open') {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        { provide: ToastService, useValue: toast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } } },
      ],
    });
    service.getGroupInvite$.and.returnValue(of(invite));
    service.getStatus$.and.returnValue(of({ currentParticipants: 1, maxCapacity: 4, status }));
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$', 'getSessions$', 'getOwnerSessionId$', 'getStatus$', 'submit$',
    ]);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    service.getSessions$.and.returnValue(of({ participants: [], total: 0 }));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
  });

  it('shows the guest link /join/{token} while the group is open', () => {
    const { fixture, element } = create();
    const input = element.querySelector<HTMLInputElement>('.invite-input');
    expect(input?.value).toBe(`${window.location.origin}/join/tok-123`);
    fixture.destroy();
  });

  it('Copy link puts the link into the clipboard and shows a toast', async () => {
    const writeText = spyOn(navigator.clipboard, 'writeText').and.resolveTo();
    const { fixture, element } = create();

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Copy link')!.click();
    // The 15 s sync timer keeps the zone busy, so wait for the clipboard promise directly.
    await new Promise((resolve) => setTimeout(resolve));

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/join/tok-123`);
    expect(toast.success).toHaveBeenCalled();
    fixture.destroy();
  });

  it('is hidden after the pre-order was sent', () => {
    const { fixture, element } = create(INVITE, 'Submitted');
    expect(element.querySelector('.invite')).toBeNull();
    fixture.destroy();
  });

  it('is hidden when the API did not return a token', () => {
    const { fixture, element } = create({ ...INVITE, inviteToken: undefined });
    expect(element.querySelector('.invite')).toBeNull();
    fixture.destroy();
  });
});
