import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'player-client-theme';

function readInitialMode(): ThemeMode {
  const stored = localStorage.getItem(STORAGE_KEY);

  if (stored === 'light' || stored === 'dark') {
    return stored;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly modeSignal = signal<ThemeMode>(readInitialMode());

  readonly mode = this.modeSignal.asReadonly();

  constructor() {
    effect(() => {
      const mode = this.modeSignal();
      document.documentElement.style.colorScheme = mode;
      localStorage.setItem(STORAGE_KEY, mode);
    });
  }

  toggle(): void {
    this.modeSignal.update((mode) => (mode === 'light' ? 'dark' : 'light'));
  }
}
