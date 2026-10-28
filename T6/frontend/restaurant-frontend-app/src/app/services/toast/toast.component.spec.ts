import { TestBed } from '@angular/core/testing';
import { MAT_SNACK_BAR_DATA, MatSnackBarRef } from '@angular/material/snack-bar';
import { ToastComponent, ToastData } from './toast.component';

function render(data: ToastData): HTMLElement {
  TestBed.configureTestingModule({
    imports: [ToastComponent],
    providers: [
      { provide: MAT_SNACK_BAR_DATA, useValue: data },
      { provide: MatSnackBarRef, useValue: jasmine.createSpyObj('MatSnackBarRef', ['dismiss']) },
    ],
  });
  const fixture = TestBed.createComponent(ToastComponent);
  fixture.detectChanges();
  document.body.appendChild(fixture.nativeElement);
  return fixture.nativeElement as HTMLElement;
}

describe('ToastComponent', () => {
  afterEach(() => document.querySelectorAll('app-toast').forEach((node) => node.remove()));

  it('without a title: icon and text on one line', () => {
    const element = render({ message: 'Pre-order sent to the restaurant', type: 'success' });
    const icon = element.querySelector('.toast-icon')!.getBoundingClientRect();
    const message = element.querySelector('.toast-message')!.getBoundingClientRect();

    expect(message.top).toBeLessThan(icon.bottom);
    expect(message.left).toBeGreaterThan(icon.right);
  });

  it('with a title: text below the title', () => {
    const element = render({ title: 'Error', message: 'Something went wrong', type: 'error' });
    const title = element.querySelector('.toast-title')!.getBoundingClientRect();
    const message = element.querySelector('.toast-message')!.getBoundingClientRect();

    expect(message.top).toBeGreaterThanOrEqual(title.bottom);
  });
});
