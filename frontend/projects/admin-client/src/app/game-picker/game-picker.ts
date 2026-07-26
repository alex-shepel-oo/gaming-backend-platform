import { Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService, GamesService, PublicGame } from 'shared';

// Shown only when a login came back scope=account -- the caller has no
// platform-wide role, so they need to say which of their games they're
// acting as an admin/moderator for before the app lets them in. Backed by
// GET /api/admin/identity/users/me/games (games the caller actually has a
// role on), not the public games list. Picking a game reuses the same
// select-game mechanism player-client's ecosystem-first login already uses --
// there is deliberately no second, admin-only version of that call.
@Component({
  selector: 'admin-game-picker',
  imports: [MatCardModule, MatProgressSpinnerModule],
  templateUrl: './game-picker.html',
  styleUrl: './game-picker.scss',
})
export class GamePicker {
  private readonly gamesService = inject(GamesService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly games = signal<PublicGame[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly entering = signal(false);
  protected readonly enterError = signal(false);

  constructor() {
    this.gamesService.listMyGames().subscribe({
      next: (games) => {
        this.games.set(games);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  protected pick(game: PublicGame): void {
    this.entering.set(true);
    this.enterError.set(false);

    this.authService.selectGame(game.id).subscribe({
      next: () => this.router.navigateByUrl('/dashboard'),
      error: () => {
        this.entering.set(false);
        this.enterError.set(true);
      },
    });
  }
}
