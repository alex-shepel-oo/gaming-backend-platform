import { TestBed } from '@angular/core/testing';
import { StatusPill } from './status-pill';

describe('StatusPill', () => {
  it('renders the label and applies the variant modifier class', () => {
    const fixture = TestBed.createComponent(StatusPill);
    fixture.componentRef.setInput('variant', 'success');
    fixture.componentRef.setInput('label', 'Completed');
    fixture.detectChanges();

    const pill = (fixture.nativeElement as HTMLElement).querySelector('.status-pill');
    expect(pill?.textContent?.trim()).toBe('Completed');
    expect(pill?.classList.contains('status-pill--success')).toBe(true);
  });

  it('switches the modifier class when the variant changes', () => {
    const fixture = TestBed.createComponent(StatusPill);
    fixture.componentRef.setInput('variant', 'error');
    fixture.componentRef.setInput('label', 'Failed');
    fixture.detectChanges();

    const pill = (fixture.nativeElement as HTMLElement).querySelector('.status-pill');
    expect(pill?.classList.contains('status-pill--error')).toBe(true);
  });
});
