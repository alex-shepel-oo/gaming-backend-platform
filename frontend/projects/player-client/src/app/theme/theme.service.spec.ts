import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

const STORAGE_KEY = 'player-client-theme';

function mockMatchMedia(prefersDark: boolean): void {
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockImplementation((query: string) => ({
      matches: prefersDark,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
}

describe('ThemeService', () => {
  afterEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
    document.documentElement.style.colorScheme = '';
  });

  it('defaults to the OS preference when nothing is stored', () => {
    mockMatchMedia(true);

    expect(TestBed.inject(ThemeService).mode()).toBe('dark');
  });

  it('defaults to light when the OS has no dark preference', () => {
    mockMatchMedia(false);

    expect(TestBed.inject(ThemeService).mode()).toBe('light');
  });

  it('remembers a stored preference over the OS default', () => {
    localStorage.setItem(STORAGE_KEY, 'dark');
    mockMatchMedia(false);

    expect(TestBed.inject(ThemeService).mode()).toBe('dark');
  });

  it('toggle() flips the mode, applies it to the document and persists it', () => {
    mockMatchMedia(false);
    const service = TestBed.inject(ThemeService);

    service.toggle();
    TestBed.tick();

    expect(service.mode()).toBe('dark');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });
});
