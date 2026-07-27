import { DatePipe } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityProfileEndpoints, UserProfile } from 'shared';
import { Profile } from './profile';

const datePipe = new DatePipe('en-US');

describe('Profile', () => {
  let httpMock: HttpTestingController;

  const profile: UserProfile = {
    id: 'user-1',
    email: 'player@example.com',
    displayName: 'Player One',
    gameId: null,
    role: 'Player',
    createdAt: '2026-01-15T00:00:00Z',
    avatarUrl: null,
    lastLoginAt: '2026-07-20T12:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Profile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows a loading state before the profile request resolves', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loading profile');

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
  });

  it('shows the real email, display name, role, join date and last login once loaded', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(profile);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Player One');
    expect(text).toContain('player@example.com');
    expect(text).toContain('Player');
    expect(text).toContain(datePipe.transform(profile.createdAt)!);
    expect(text).toContain(datePipe.transform(profile.lastLoginAt)!);
  });

  it('shows an absent-state note for last login when the user has never logged in', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush({ ...profile, lastLoginAt: null });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No login recorded yet');
  });

  it('shows an error message when the profile request fails', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("couldn't read your account details");
  });

  it('submits the edit form and calls updateMe with the entered display name and avatar url', () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === IdentityProfileEndpoints.me && req.method === 'GET').flush(profile);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const inputs = element.querySelectorAll('form.profile-edit-form input');
    const displayNameInput = inputs[0] as HTMLInputElement;
    const avatarUrlInput = inputs[1] as HTMLInputElement;

    displayNameInput.value = 'New Name';
    displayNameInput.dispatchEvent(new Event('input'));
    avatarUrlInput.value = 'https://example.com/a.png';
    avatarUrlInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (element.querySelector('form.profile-edit-form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { cancelable: true }),
    );

    const req = httpMock.expectOne((r) => r.url === IdentityProfileEndpoints.me && r.method === 'PATCH');
    expect(req.request.body).toEqual({ displayName: 'New Name', avatarUrl: 'https://example.com/a.png' });

    req.flush({ ...profile, displayName: 'New Name', avatarUrl: 'https://example.com/a.png' });
    fixture.detectChanges();

    const text = element.textContent ?? '';
    expect(text).toContain('Profile updated.');
    expect(text).toContain('New Name');
  });
});
