import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { GameSelectionService, IdentityAuthEndpoints } from 'shared';
import { Shell } from './shell';

describe('Shell', () => {
  let httpMock: HttpTestingController;
  let router: Router;
  let gameSelection: GameSelectionService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    gameSelection = TestBed.inject(GameSelectionService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('renders navigation links to the three authenticated screens', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Games');
    expect(text).toContain('Wallet');
    expect(text).toContain('Convert');
  });

  it('shows the selected game once one has been picked', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    let text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Demo Shooter');

    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' });
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
  });

  it('logs out, clears the selected game, and redirects to Login', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    gameSelection.select({ id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' });

    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    const logoutButton = Array.from(buttons).find((button) => button.textContent?.includes('Log out'));
    logoutButton!.dispatchEvent(new Event('click'));

    httpMock.expectOne(IdentityAuthEndpoints.logout).flush(null);

    expect(gameSelection.selected()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith('/login');
  });
});
