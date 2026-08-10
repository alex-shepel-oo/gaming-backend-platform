import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService, GamesService, Loadable, PublicGame } from 'shared';

// Shown only when a login came back scope=account: the caller has no
// platform-wide role, so they need to say which of their games they're
// acting as an admin/moderator for before the app lets them in. Backed by
// GET /api/admin/identity/users/me/games, not the public games list.
// Picking a game reuses the same select-game mechanism player-client's
// ecosystem-first login already uses, no separate admin-only call.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'admin-game-picker',
  imports: [MatCardModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './game-picker.html',
  styleUrl: './game-picker.scss',
})
export class GamePicker {
  private readonly gamesService = inject(GamesService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly games = signal<PublicGame[]>([]);
  private readonly gamesResource = new Loadable();
  protected readonly loading = this.gamesResource.loading;
  protected readonly loadError = this.gamesResource.error;
  protected readonly entering = signal(false);
  protected readonly enterError = signal(false);

  constructor() {
    this.gamesResource.load(this.gamesService.listMyGames(), (games) => this.games.set(games));
  }

  protected pick(game: PublicGame): void {
    this.entering.set(true);
    this.enterError.set(false);

    this.authService.selectGame(game.id).subscribe({
      next: () => this.router.navigateByUrl('/users'),
      error: () => {
        this.entering.set(false);
        this.enterError.set(true);
      },
    });
  }
}
