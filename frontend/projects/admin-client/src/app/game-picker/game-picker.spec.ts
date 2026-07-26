import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { IdentityAuthEndpoints, IdentityGameEndpoints } from 'shared';
import { GamePicker } from './game-picker';

@Component({ selector: 'test-stub', template: '' })
class RouteStub {}

describe('GamePicker', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  const myGames = [
    { id: 'game-1', slug: 'space-invaders', name: 'Space Invaders' },
    { id: 'game-2', slug: 'pac-man', name: 'Pac Man' },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GamePicker],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Real route target -- see admin-login.spec.ts for why [] isn't safe here.
        provideRouter([{ path: 'dashboard', component: RouteStub }]),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists the games returned by users/me/games, not the public games list', () => {
    const fixture = TestBed.createComponent(GamePicker);

    httpMock.expectOne(IdentityGameEndpoints.myGames).flush(myGames);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
    expect(text).toContain('Pac Man');
  });

  it('shows an empty state when the caller has no game roles', () => {
    const fixture = TestBed.createComponent(GamePicker);

    httpMock.expectOne(IdentityGameEndpoints.myGames).flush([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("don't have a role in any game yet");
  });

  it('selecting a game calls the existing select-game endpoint and navigates to the dashboard', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    const fixture = TestBed.createComponent(GamePicker);

    httpMock.expectOne(IdentityGameEndpoints.myGames).flush(myGames);
    fixture.detectChanges();

    const card = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-card')).find((element) =>
      element.textContent?.includes('Pac Man'),
    ) as HTMLElement;
    card.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    const selectGameRequest = httpMock.expectOne(IdentityAuthEndpoints.selectGame);
    expect(selectGameRequest.request.body).toEqual({ gameId: 'game-2' });
    selectGameRequest.flush({ accessToken: 'the-game-scoped-access-token' });

    expect(navigateSpy).toHaveBeenCalledWith('/dashboard');
  });
});
