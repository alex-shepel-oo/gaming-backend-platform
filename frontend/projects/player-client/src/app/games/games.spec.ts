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

  it('renders the list returned by games/public', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
    expect(text).toContain('Pac Man');
  });

  it('updates the shared game-selection state when a game is picked', () => {
    const fixture = TestBed.createComponent(Games);

    httpMock.expectOne(IdentityGameEndpoints.publicGames).flush(publicGames);
    fixture.detectChanges();

    expect(gameSelection.selected()).toBeNull();

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    (buttons[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(gameSelection.selected()).toEqual(publicGames[1]);
  });
});
