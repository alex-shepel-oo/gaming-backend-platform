import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { GameSelectionService, GamesService, PublicGame } from 'shared';

@Component({
  selector: 'app-games',
  imports: [MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './games.html',
  styleUrl: './games.scss',
})
export class Games {
  private readonly gamesService = inject(GamesService);
  protected readonly gameSelection = inject(GameSelectionService);

  protected readonly games = signal<PublicGame[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

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

  protected select(game: PublicGame): void {
    this.gameSelection.select(game);
  }

  protected isSelected(game: PublicGame): boolean {
    return this.gameSelection.selected()?.id === game.id;
  }
}
