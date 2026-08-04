import { Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService, GameSelectionService, GamesService, PublicGame } from 'shared';
import { GameDetailsDialog } from './game-details-dialog/game-details-dialog';

@Component({
  selector: 'app-games',
  imports: [MatCardModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './games.html',
  styleUrl: './games.scss',
})
export class Games {
  private readonly gamesService = inject(GamesService);
  private readonly authService = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  protected readonly gameSelection = inject(GameSelectionService);

  protected readonly games = signal<PublicGame[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly entering = signal(false);
  protected readonly enterError = signal(false);

  constructor() {
    this.gamesService.listPublicGames().subscribe({
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

  protected openDetails(game: PublicGame): void {
    this.dialog
      .open(GameDetailsDialog, {
        data: { game, isSelected: this.isSelected(game) },
        width: '420px',
        enterAnimationDuration: '0ms',
        exitAnimationDuration: '0ms',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result === 'select') {
          this.gameSelection.select(game);
          this.enterGame(game);
        }
      });
  }

  protected isSelected(game: PublicGame): boolean {
    return this.gameSelection.selected()?.id === game.id;
  }

  private enterGame(game: PublicGame): void {
    this.entering.set(true);
    this.enterError.set(false);

    this.authService.selectGame(game.id).subscribe({
      next: () => {
        this.entering.set(false);
        this.router.navigateByUrl('/wallet');
      },
      error: () => {
        this.entering.set(false);
        this.enterError.set(true);
      },
    });
  }
}
