import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';

describe('EmptyState', () => {
  it('renders the required message with the default icon', () => {
    const fixture = TestBed.createComponent(EmptyState);
    fixture.componentRef.setInput('message', 'No games yet');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('No games yet');
    expect(host.querySelector('mat-icon')?.textContent).toBe('inbox');
  });

  it('renders a custom icon when provided', () => {
    const fixture = TestBed.createComponent(EmptyState);
    fixture.componentRef.setInput('message', 'No results');
    fixture.componentRef.setInput('icon', 'search_off');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('mat-icon')?.textContent).toBe('search_off');
  });
});
