import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityProfileEndpoints } from './identity-profile-endpoints';
import { UserProfile } from './profile.service';
import { ProfileService } from './profile.service';

describe('ProfileService', () => {
  let httpMock: HttpTestingController;
  let service: ProfileService;

  const profile: UserProfile = {
    id: 'user-1',
    email: 'player@example.com',
    displayName: 'Player One',
    gameId: null,
    role: 'Player',
    createdAt: '2026-01-01T00:00:00Z',
    avatarUrl: null,
    lastLoginAt: '2026-07-20T12:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ProfileService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with no cached profile', () => {
    expect(service.profile()).toBeNull();
  });

  it('populates the shared signal after refreshProfile resolves', () => {
    service.refreshProfile().subscribe();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me && req.method === 'GET').flush(profile);

    expect(service.profile()).toEqual(profile);
  });

  it('populates the shared signal after updateMe resolves', () => {
    const updated: UserProfile = { ...profile, displayName: 'New Name', avatarUrl: 'https://example.com/a.png' };

    service.updateMe({ displayName: 'New Name', avatarUrl: 'https://example.com/a.png' }).subscribe();

    const req = httpMock.expectOne((r) => r.url === IdentityProfileEndpoints.me && r.method === 'PATCH');
    expect(req.request.body).toEqual({ displayName: 'New Name', avatarUrl: 'https://example.com/a.png' });
    req.flush(updated);

    expect(service.profile()).toEqual(updated);
  });

  it('clearProfile resets the signal back to null', () => {
    service.refreshProfile().subscribe();
    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);

    service.clearProfile();

    expect(service.profile()).toBeNull();
  });
});
