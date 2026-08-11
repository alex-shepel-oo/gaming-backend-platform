import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { finalize, shareReplay } from 'rxjs/operators';
import { AuthService } from './auth.service';

// Collapses concurrent refresh attempts into a single in-flight HTTP call.
// Without this, two requests hitting 401 around the same time each fire
// their own POST /auth/refresh with the same not-yet-rotated refresh
// cookie -- the second one lands after the first has already rotated it,
// the backend treats that as reuse of a stale token, and the still-valid
// session gets logged out by its own second refresh attempt.
@Injectable({ providedIn: 'root' })
export class TokenRefreshCoordinator {
  private readonly authService = inject(AuthService);
  private inFlight: Observable<void> | null = null;

  refresh(): Observable<void> {
    this.inFlight ??= this.authService.refresh().pipe(
      finalize(() => (this.inFlight = null)),
      shareReplay(1),
    );

    return this.inFlight;
  }
}
