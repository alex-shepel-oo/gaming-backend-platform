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
});
