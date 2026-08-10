import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Landing } from './landing';

describe('Landing', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Landing],
      providers: [provideRouter([])],
    });
  });

  it('links the demo CTA to the login route and the portfolio CTA to shepel.dev', () => {
    const fixture = TestBed.createComponent(Landing);
    fixture.detectChanges();

    const links = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.landing__actions a'));
    expect(links).toHaveLength(2);
    expect(links[0].getAttribute('href')).toBe('/login');
    expect(links[1].getAttribute('href')).toBe('https://shepel.dev');
    expect(links[1].getAttribute('target')).toBe('_blank');
    expect(links[1].getAttribute('rel')).toBe('noopener noreferrer');
  });
});
