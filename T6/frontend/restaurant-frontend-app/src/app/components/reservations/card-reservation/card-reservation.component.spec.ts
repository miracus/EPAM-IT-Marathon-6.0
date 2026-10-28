import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CardReservationComponent } from './card-reservation.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';

const RESERVATION: ReservationCustomer = {
  id: 'res-1',
  tableId: 'table-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 2,
  status: ReservationStatusCustomer.Reserved,
  restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48',
  waiterId: 'waiter-1',
};

describe('CardReservationComponent', () => {
  let component: CardReservationComponent;
  let fixture: ComponentFixture<CardReservationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }, provideRouter([])],
      imports: [CardReservationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CardReservationComponent);
    component = fixture.componentInstance;
    // r — обов'язковий input: без нього компонент падає з NG0950
    fixture.componentRef.setInput('r', RESERVATION);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Group Pre-Order', () => {
    const groupButton = () =>
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
        (button) => button.textContent?.trim() === 'Group Pre-Order',
      );

    it('is shown to the customer for a reserved table with 2+ guests', () => {
      expect(groupButton()).toBeDefined();
    });

    it('is hidden for a reservation for one guest', () => {
      fixture.componentRef.setInput('r', { ...RESERVATION, guestCount: 1 });
      fixture.detectChanges();
      expect(groupButton()).toBeUndefined();
    });

    it('opens /reservations/{id}/group-order', () => {
      const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      groupButton()!.click();
      expect(navigate).toHaveBeenCalledWith(['/reservations', 'res-1', 'group-order']);
    });
  });
});
