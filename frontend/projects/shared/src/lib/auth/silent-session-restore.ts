import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { catchError, firstValueFrom, of } from 'rxjs';
import { AuthService } from './auth.service';

export function restoreSessionSilently(authService: AuthService): Promise<void> {
  return firstValueFrom(authService.refresh().pipe(catchError(() => of(undefined))));
}

export function provideSilentSessionRestore(): EnvironmentProviders {
  return provideAppInitializer(() => restoreSessionSilently(inject(AuthService)));
}
