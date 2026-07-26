import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GamesService } from './games.service';
import { IdentityGameEndpoints } from './identity-game-endpoints';

describe('GamesService', () => {
  let httpMock: HttpTestingController;
  let service: GamesService;

  const games = [{ id: 'game-1', slug: 'space-invaders', name: 'Space Invaders' }];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(GamesService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('listMyGames requests the caller-scoped games list, not the public one', () => {
    let result: unknown;
    service.listMyGames().subscribe((games) => (result = games));

    const request = httpMock.expectOne(IdentityGameEndpoints.myGames);
    expect(request.request.method).toBe('GET');
    request.flush(games);

    expect(result).toEqual(games);
  });

  const allGames = [
    { id: 'game-1', slug: 'space-invaders', name: 'Space Invaders', isActive: true, createdAt: '2026-01-01T00:00:00Z' },
  ];

  it('listAllGames requests the platform-admin games list', () => {
    let result: unknown;
    service.listAllGames().subscribe((games) => (result = games));

    const request = httpMock.expectOne(IdentityGameEndpoints.allGames);
    expect(request.request.method).toBe('GET');
    request.flush(allGames);

    expect(result).toEqual(allGames);
  });

  it('createGame posts the new game and returns the created record', () => {
    let result: unknown;
    service.createGame({ slug: 'pac-man', name: 'Pac Man' }).subscribe((game) => (result = game));

    const request = httpMock.expectOne(IdentityGameEndpoints.allGames);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ slug: 'pac-man', name: 'Pac Man' });
    request.flush(allGames[0]);

    expect(result).toEqual(allGames[0]);
  });

  it('updateGame patches the game by id', () => {
    let result: unknown;
    service.updateGame('game-1', { isActive: false }).subscribe((game) => (result = game));

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ ...allGames[0], isActive: false });

    expect(result).toEqual({ ...allGames[0], isActive: false });
  });
});
