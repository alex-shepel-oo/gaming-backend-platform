import { TestBed } from '@angular/core/testing';
import { NotAvailable } from './not-available';

describe('NotAvailable', () => {
  it('shows a default message when none is provided', () => {
    const fixture = TestBed.createComponent(NotAvailable);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Not available yet');
  });

  it('shows a custom message when provided', () => {
    const fixture = TestBed.createComponent(NotAvailable);
    fixture.componentRef.setInput('message', 'Join date not available yet');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Join date not available yet');
  });
});
