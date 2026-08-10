import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CurrencyScope, EconomyEndpoints, IdentityGameEndpoints } from 'shared';
import { Games } from './games';

describe('Games', () => {
  let httpMock: HttpTestingController;

  const publicGames = [
    { id: 'game-1', slug: 'space-invaders', name: 'Space Invaders', description: null, iconUrl: null },
    { id: 'game-2', slug: 'pac-man', name: 'Pac Man', description: 'Classic arcade maze chase.', iconUrl: null },
  ];

  function configureWithBreakpoint(matches: boolean): void {
    TestBed.configureTestingModule({
      imports: [Games],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches, breakpoints: {} }) } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
  }

  function flushInitialRequests(balances: unknown[] = [], games: unknown[] = publicGames): void {
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush(balances);
    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(games);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('shows a loading state before the games/public response arrives', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loading games');

    flushInitialRequests();
  });

  it('renders the list returned by games/public', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
    expect(text).toContain('Pac Man');
  });

  it('shows an empty state when no games are returned', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests([], []);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No games are available right now.');
  });

  it('shows an error state when the games/public request fails', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    httpMock.expectOne((req) => req.url === EconomyEndpoints.balances).flush([]);
    httpMock
      .expectOne(IdentityGameEndpoints.publicGames)
      .flush({ status: 500, title: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't load the games list");
  });

  it('shows balance and description directly on the card at desktop widths', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests([
      { currencyId: 'c1', currencyCode: 'PACMAN_COINS', scope: CurrencyScope.Game, gameId: 'game-2', amount: 12, iconUrl: null },
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('12 PACMAN_COINS');
    expect(text).toContain('Classic arcade maze chase.');
  });

  it('hides the balance badge entirely when the game balance is zero', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests([
      { currencyId: 'c1', currencyCode: 'PACMAN_COINS', scope: CurrencyScope.Game, gameId: 'game-2', amount: 0, iconUrl: null },
    ]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.game-card__balance')).toBeNull();
    expect(element.textContent).not.toContain('PACMAN_COINS');
  });

  it('falls back to the placeholder hero when a game icon fails to load', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests([], [{ ...publicGames[1], iconUrl: 'https://example.test/broken.png' }]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const image = element.querySelector('img.game-card__hero-image') as HTMLImageElement;
    expect(image).not.toBeNull();

    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img.game-card__hero-image')).toBeNull();
    expect(element.querySelector('.game-card__hero-placeholder')).not.toBeNull();
  });

  it('does not open a dialog when a card is clicked at desktop widths', () => {
    configureWithBreakpoint(false);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests();
    fixture.detectChanges();

    const card = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.game-card')).find((element) =>
      element.textContent?.includes('Pac Man'),
    ) as HTMLElement;
    card.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(document.body.textContent ?? '').not.toContain('About this game');
  });

  it('opens a read-only details dialog on tap at mobile widths', () => {
    configureWithBreakpoint(true);
    const fixture = TestBed.createComponent(Games);
    flushInitialRequests();
    fixture.detectChanges();

    const card = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.game-card')).find((element) =>
      element.textContent?.includes('Pac Man'),
    ) as HTMLElement;
    card.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    const dialogText = document.body.textContent ?? '';
    expect(dialogText).toContain('Pac Man');
    expect(dialogText).toContain('pac-man');
    expect(dialogText).not.toContain('Select this game');
  });
});
