import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityGameEndpoints } from 'shared';
import { GamesManagement } from './games-management';

describe('GamesManagement', () => {
  let httpMock: HttpTestingController;

  const games = [
    { id: 'game-1', slug: 'space-invaders', name: 'Space Invaders', isActive: true, createdAt: '2026-01-01T00:00:00Z' },
    { id: 'game-2', slug: 'pac-man', name: 'Pac Man', isActive: false, createdAt: '2026-01-02T00:00:00Z' },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GamesManagement],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('renders every game returned by the platform-admin games list', () => {
    const fixture = TestBed.createComponent(GamesManagement);

    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Space Invaders');
    expect(text).toContain('Pac Man');
  });

  it('rejects a create-form slug over 100 characters and a name over 200 characters', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['createForm'].setValue({ slug: 'a'.repeat(101), name: 'a'.repeat(201) });

    expect(component['createForm'].controls.slug.hasError('maxlength')).toBe(true);
    expect(component['createForm'].controls.name.hasError('maxlength')).toBe(true);
    expect(component['createForm'].invalid).toBe(true);
  });

  it('rejects an edit-form description over 2000 characters', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['startEdit'](games[0] as never);
    component['editForm'].controls.description.setValue('a'.repeat(2001));

    expect(component['editForm'].controls.description.hasError('maxlength')).toBe(true);
    expect(component['editForm'].invalid).toBe(true);
  });

  it('creating a game calls the create endpoint and adds it to the list', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['createForm'].setValue({ slug: 'tetris', name: 'Tetris' });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('form.create-form')!.dispatchEvent(new Event('submit'));

    const request = httpMock.expectOne(IdentityGameEndpoints.allGames);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ slug: 'tetris', name: 'Tetris' });
    request.flush({ id: 'game-3', slug: 'tetris', name: 'Tetris', isActive: true, createdAt: '2026-01-03T00:00:00Z' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Tetris');
  });

  it('editing a game calls the update endpoint with the id and the edited fields', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const editButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[aria-label="Edit"]',
    ) as HTMLButtonElement;
    editButton.click();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['editForm'].controls.isActive.setValue(true);

    const saveButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Save'),
    ) as HTMLButtonElement;
    saveButton.click();

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ name: 'Space Invaders', isActive: true, description: '', iconUrl: '' });
    request.flush({ ...games[0], isActive: true });
  });

  it('editing a game round-trips description and iconUrl through the save endpoint', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const editButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[aria-label="Edit"]',
    ) as HTMLButtonElement;
    editButton.click();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component['editForm'].controls.description.setValue('Classic arcade shooter');
    component['editForm'].controls.iconUrl.setValue('https://example.com/icon.png');

    const saveButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Save'),
    ) as HTMLButtonElement;
    saveButton.click();

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({
      name: 'Space Invaders',
      isActive: true,
      description: 'Classic arcade shooter',
      iconUrl: 'https://example.com/icon.png',
    });

    request.flush({
      ...games[0],
      description: 'Classic arcade shooter',
      iconUrl: 'https://example.com/icon.png',
    });
    fixture.detectChanges();

    // The panel closes on save; reopening it re-populates the form from the
    // updated game in the list, which proves the new values actually stuck
    // rather than just having been sent on the wire.
    const reopenButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[aria-label="Edit"]',
    ) as HTMLButtonElement;
    reopenButton.click();
    fixture.detectChanges();

    expect(component['editForm'].controls.description.value).toBe('Classic arcade shooter');
    expect(component['editForm'].controls.iconUrl.value).toBe('https://example.com/icon.png');
  });

  it('toggling an active game calls updateGame with isActive flipped to false', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const toggleButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[aria-label="Deactivate"]',
    ) as HTMLButtonElement;
    toggleButton.click();

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isActive: false });

    request.flush({ ...games[0], isActive: false });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Inactive');
  });

  it('disables the delete button while the game is still active', () => {
    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const deleteButtons = (fixture.nativeElement as HTMLElement).querySelectorAll('button[aria-label="Delete"]');
    expect((deleteButtons[0] as HTMLButtonElement).disabled).toBe(true);
    expect((deleteButtons[1] as HTMLButtonElement).disabled).toBe(false);
  });

  it('deleting an inactive game asks for confirmation, then calls the delete endpoint and removes it from the list', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const deleteButtons = (fixture.nativeElement as HTMLElement).querySelectorAll('button[aria-label="Delete"]');
    (deleteButtons[1] as HTMLButtonElement).click();

    expect(window.confirm).toHaveBeenCalled();

    const request = httpMock.expectOne(IdentityGameEndpoints.game('game-2'));
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Pac Man');
  });

  it('does not call the delete endpoint when the confirmation is declined', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    const fixture = TestBed.createComponent(GamesManagement);
    httpMock.expectOne(IdentityGameEndpoints.allGames).flush(games);
    fixture.detectChanges();

    const deleteButtons = (fixture.nativeElement as HTMLElement).querySelectorAll('button[aria-label="Delete"]');
    (deleteButtons[1] as HTMLButtonElement).click();

    httpMock.expectNone(IdentityGameEndpoints.game('game-2'));
  });
});
