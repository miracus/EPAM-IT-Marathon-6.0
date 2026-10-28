import { TestBed } from '@angular/core/testing';
import { Participant } from '@app/models/group-orders/participant.model';
import { KitchenViewComponent } from './kitchen-view.component';

describe('KitchenViewComponent (US18 AC4, US19 AC4)', () => {
  function render(participants: Participant[]): HTMLElement {
    const fixture = TestBed.createComponent(KitchenViewComponent);
    fixture.componentRef.setInput('participants', participants);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows allergies on top and groups dishes by serving time with notes', () => {
    const element = render([
      {
        id: 'a', type: 'Guest', name: 'Maksym', total: 28, isSubmitted: true, allergies: 'Nut allergy',
        items: [
          { dish: { id: 'salad', name: 'Spring Salad', price: 19, dishType: 'Appetizer' }, quantity: 1, note: 'No onion' },
          { dish: { id: 'cake', name: 'Berry Cheesecake', price: 9, dishType: 'Dessert' }, quantity: 1 },
        ],
      },
    ]);

    expect(element.querySelector('.allergy-banner')?.textContent).toContain('Nut allergy — Maksym. Kitchen will be warned.');
    const titles = [...element.querySelectorAll('.section-title')].map((title) => title.textContent?.trim());
    expect(titles).toEqual(['On arrival', 'After mains']);
    expect(element.textContent).toContain('“No onion” — Maksym');
  });

  it('empty state when nobody has submitted', () => {
    expect(render([]).textContent).toContain('No submitted carts yet');
  });
});
