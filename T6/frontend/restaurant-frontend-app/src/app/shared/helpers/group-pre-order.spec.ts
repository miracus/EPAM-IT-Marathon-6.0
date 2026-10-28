import { Participant } from '@app/models/group-orders/participant.model';
import {
  buildKitchenView,
  defaultServeTime,
  findNewItems,
  formatTimeLeft,
  guestsWithoutDishes,
  isCustomServeTime,
  progressLabel,
} from './group-pre-order';

const salad = { id: 'salad', name: 'Spring Salad', price: 19, dishType: 'Appetizer' };
const cake = { id: 'cake', name: 'Berry Cheesecake', price: 9, dishType: 'Dessert' };

function person(id: string, name: string, items: Participant['items'], extra: Partial<Participant> = {}): Participant {
  return { id, name, type: 'Guest', items, total: 0, isSubmitted: true, ...extra };
}

describe('group pre-order helpers', () => {
  describe('formatTimeLeft (US13)', () => {
    const cases: [number, string | null][] = [
      [0, null],
      [-5, null],
      [30, 'in less than a minute'],
      [60, 'in 1m'],
      [59 * 60 + 59, 'in 59m'],
      [3600, 'in 1h'],
      [80 * 60, 'in 1h 20m'],
    ];
    for (const [seconds, expected] of cases) {
      it(`${seconds} s → ${expected}`, () => expect(formatTimeLeft(seconds)).toBe(expected));
    }
  });

  it('desserts default to After mains, the rest to On arrival (US19 AC2)', () => {
    expect(defaultServeTime('Dessert')).toBe('AfterMains');
    expect(defaultServeTime('Main course')).toBe('OnArrival');
    expect(defaultServeTime(undefined)).toBe('OnArrival');
  });

  it('a serving time is "custom" only when it differs from the default (US19 AC3)', () => {
    expect(isCustomServeTime({ dish: cake, quantity: 1, serveTime: 'AfterMains' })).toBeFalse();
    expect(isCustomServeTime({ dish: cake, quantity: 1, serveTime: 'OnArrival' })).toBeTrue();
    expect(isCustomServeTime({ dish: salad, quantity: 1, serveTime: 'AfterMains' })).toBeTrue();
  });

  it('progress: "3 of 5 joined · 2 chose dishes" (US20 AC4)', () => {
    const people = [person('a', 'Anna', [{ dish: salad, quantity: 1 }]), person('b', 'Olena', [{ dish: cake, quantity: 1 }]), person('c', 'Iryna', [])];
    expect(progressLabel(people, 5)).toBe('3 of 5 joined · 2 chose dishes');
    expect(guestsWithoutDishes(people).map((p) => p.name)).toEqual(['Iryna']);
  });

  it('finds new dishes and grown quantities, not removed ones (US20 AC2)', () => {
    const before = [person('a', 'Anna', [{ dish: salad, quantity: 1 }, { dish: cake, quantity: 1 }])];
    const after = [
      person('a', 'Anna', [{ dish: salad, quantity: 2 }]),
      person('b', 'Olena', [{ dish: cake, quantity: 1 }]),
    ];

    expect(findNewItems(before, after).map((item) => `${item.participantName} ${item.dishName}`)).toEqual([
      'Anna Spring Salad',
      'Olena Berry Cheesecake',
    ]);
    expect(findNewItems(after, after)).toEqual([]);
  });

  it('kitchen view: submitted carts by serving time, notes with author, allergies on top (US18/US19)', () => {
    const view = buildKitchenView([
      person('a', 'Anna', [{ dish: salad, quantity: 2, note: 'No onion' }, { dish: cake, quantity: 1, serveTime: 'AfterMains' }], {
        allergies: 'Nuts',
      }),
      person('b', 'Olena', [{ dish: salad, quantity: 1, serveTime: 'OnArrival' }]),
      person('c', 'Maksym', [{ dish: cake, quantity: 5 }], { isSubmitted: false }),
    ]);

    expect(view.allergies).toEqual([{ name: 'Anna', allergies: 'Nuts' }]);
    expect(view.sections.map((section) => section.label)).toEqual(['On arrival', 'After mains']);
    const saladLine = view.sections[0].lines[0];
    expect(saladLine.quantity).toBe(3);
    expect(saladLine.total).toBe(57);
    expect(saladLine.notes).toEqual([{ author: 'Anna', note: 'No onion' }]);
    // Maksym has not submitted: his 5 cakes are not in the kitchen view.
    expect(view.sections[1].lines[0].quantity).toBe(1);
  });
});
