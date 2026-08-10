import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotFound } from './not-found';

describe('NotFound', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotFound],
      providers: [provideRouter([])],
    });
  });

  it('links to / by default', () => {
    const fixture = TestBed.createComponent(NotFound);
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a');
    expect(link?.getAttribute('href')).toBe('/');
    expect(link?.textContent).toContain('Back to safety');
  });

  it('links to a custom home path and label when provided', () => {
    const fixture = TestBed.createComponent(NotFound);
    fixture.componentRef.setInput('homeLink', '/dashboard');
    fixture.componentRef.setInput('homeLabel', 'Return to dashboard');
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a');
    expect(link?.getAttribute('href')).toBe('/dashboard');
    expect(link?.textContent).toContain('Return to dashboard');
  });
});
