import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GameSelectionService, IdentityGameEndpoints } from 'shared';
import { Games } from './games';

describe('Games', () => {
  let httpMock: HttpTestingController;
  let gameSelection: GameSelectionService;

  const publicGames = [
    { id: 'game-1', slug: 'space-invaders', name: 'Space Invaders' },
    { id: 'game-2', slug: 'pac-man', name: 'Pac Man' },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Games],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    gameSelection = TestBed.inject(GameSelectionService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows a loading state before the games/public response arrives', () => {
    const fixture = TestBed.createComponent(Games);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loading games');

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
  });

  it('renders the list returned by games/public', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
    expect(text).toContain('Pac Man');
  });

  it('shows an empty state when no games are returned', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No games are available right now.');
  });

  it('shows an error state when the games/public request fails', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock
      .expectOne(IdentityGameEndpoints.publicGames)
      .flush({ status: 500, title: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't load the games list");
  });

  function openDialogFor(fixture: { nativeElement: unknown; detectChanges: () => void }, gameName: string): void {
    const card = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-card')).find((element) =>
      element.textContent?.includes(gameName),
    ) as HTMLElement;

    card.dispatchEvent(new Event('click'));
    fixture.detectChanges();
  }

  it('opens a details dialog with the game name and slug when a card is clicked', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
    fixture.detectChanges();

    openDialogFor(fixture, 'Pac Man');

    const dialogText = document.body.textContent ?? '';
    expect(dialogText).toContain('Pac Man');
    expect(dialogText).toContain('pac-man');
    expect(dialogText).toContain("isn't available yet");
  });

  it('updates the shared game-selection state when "Select this game" is clicked in the dialog', async () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
    fixture.detectChanges();

    expect(gameSelection.selected()).toBeNull();

    openDialogFor(fixture, 'Pac Man');

    const selectButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Select this game'),
    ) as HTMLButtonElement;
    selectButton.click();

    // Dialog close (even with animations disabled) resolves afterClosed()
    // through a microtask chain, not synchronously -- see MatDialogContainer.
    // A macrotask tick flushes the whole microtask queue ahead of it.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(gameSelection.selected()).toEqual(publicGames[1]);
  });
});
