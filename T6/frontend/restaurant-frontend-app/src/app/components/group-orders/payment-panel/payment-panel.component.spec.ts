import { TestBed } from '@angular/core/testing';
import { PaymentView } from '@app/models/group-orders/group-payment.model';
import { PaymentPanelComponent } from './payment-panel.component';

describe('PaymentPanelComponent (US17)', () => {
  const VIEW: PaymentView = {
    mode: 'EachPaysOwn',
    grandTotal: 69,
    paidTotal: 38,
    pendingTotal: 31,
    participants: [
      { id: 'owner', type: 'Owner', name: 'Oleh', ownDishesTotal: 9, amount: 9, isPaid: false },
      { id: 'anna', type: 'Guest', name: 'Anna', ownDishesTotal: 38, amount: 38, isPaid: true },
      { id: 'olena', type: 'Guest', name: 'Olena', ownDishesTotal: 22, amount: 22, isPaid: false },
    ],
  };

  function create(view: PaymentView) {
    const fixture = TestBed.createComponent(PaymentPanelComponent);
    fixture.componentRef.setInput('view', view);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows three modes, shares with Paid / Pending and the totals', () => {
    const { element } = create(VIEW);
    expect(element.querySelectorAll('[role="radio"]').length).toBe(3);
    expect(element.querySelector('[aria-checked="true"]')?.textContent).toContain('Each pays own');
    expect(element.textContent).toContain('Anna');
    expect(element.textContent).toContain('Paid');
    expect(element.textContent).toContain('Pending');
    expect(element.querySelector('.totals')?.textContent).toContain('Paid $38.00 · Pending $31.00');
    expect(element.textContent).toContain('Remind 1');
  });

  it('emits the chosen mode and "Mark paid"', () => {
    const { fixture, element } = create(VIEW);
    const modes: string[] = [];
    const paid: string[] = [];
    fixture.componentInstance.modeChange.subscribe((mode) => modes.push(mode));
    fixture.componentInstance.markPaid.subscribe((person) => paid.push(person.id));

    (element.querySelectorAll('[role="radio"]')[2] as HTMLButtonElement).click();
    (element.querySelector('.link') as HTMLButtonElement).click();

    expect(modes).toEqual(['SplitEqually']);
    expect(paid).toEqual(['owner']);
  });

  it('"I pay for all": guests are covered by the organizer', () => {
    const { element } = create({
      ...VIEW,
      mode: 'OwnerPaysAll',
      participants: VIEW.participants.map((p) => ({ ...p, amount: p.type === 'Owner' ? 69 : 0, isPaid: p.type !== 'Owner' })),
    });
    expect(element.textContent).toContain('Covered by organizer');
  });
});
