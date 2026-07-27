import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityGameEndpoints, TokenStore } from 'shared';
import { MyGameMetadata } from './my-game-metadata';

function fakeAccessToken(options: { permissions?: string[]; gameId?: string | null } = {}): string {
  const { permissions = [], gameId = null } = options;
  const payload = {
    sub: 'admin-1',
    email: 'gameadmin@example.com',
    name: 'Game Admin',
    scope: gameId ? 'game' : 'platform',
    role: null,
    perms: permissions,
    game_id: gameId,
  };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

const myGame = {
  id: 'game-1',
  slug: 'space-invaders',
  name: 'Space Invaders',
  description: 'Defend the earth.',
  iconUrl: 'https://cdn.example.com/space-invaders.png',
};

describe('MyGameMetadata', () => {
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyGameMetadata],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the caller\'s own game via listMyGames and pre-fills the two-field form', () => {
    tokenStore.set(fakeAccessToken({ permissions: ['game.metadata.edit'], gameId: 'game-1' }));

    const fixture = TestBed.createComponent(MyGameMetadata);
    httpMock.expectOne(IdentityGameEndpoints.myGames).flush([myGame]);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component['editForm'].getRawValue()).toEqual({
      description: 'Defend the earth.',
      iconUrl: 'https://cdn.example.com/space-invaders.png',
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
  });

  it('submitting the form calls updateGame with only description and iconUrl', () => {
    tokenStore.set(fakeAccessToken({ permissions: ['game.metadata.edit'], gameId: 'game-1' }));

    const fixture = TestBed.createComponent(MyGameMetadata);
    httpMock.expectOne(IdentityGameEndpoints.myGames).flush([myGame]);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['editForm'].setValue({ description: 'Updated blurb', iconUrl: 'https://cdn.example.com/new-icon.png' });

    (fixture.nativeElement as HTMLElement).querySelector('form.edit-form')!.dispatchEvent(new Event('submit'));

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ description: 'Updated blurb', iconUrl: 'https://cdn.example.com/new-icon.png' });
    request.flush({ ...myGame, description: 'Updated blurb', iconUrl: 'https://cdn.example.com/new-icon.png' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Saved.');
  });

  it('does not crash and shows an empty state when listMyGames returns no games', () => {
    tokenStore.set(fakeAccessToken({ permissions: ['game.metadata.edit'], gameId: 'game-1' }));

    const fixture = TestBed.createComponent(MyGameMetadata);
    httpMock.expectOne(IdentityGameEndpoints.myGames).flush([]);
    fixture.detectChanges();

    expect(() => fixture.detectChanges()).not.toThrow();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Space Invaders');
  });

  it('does not crash and skips the request when the caller has no gameId', () => {
    tokenStore.set(fakeAccessToken({ permissions: ['game.metadata.edit'], gameId: null }));

    const fixture = TestBed.createComponent(MyGameMetadata);

    expect(() => fixture.detectChanges()).not.toThrow();
    httpMock.verify();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("isn't scoped to a single game");
  });
});
