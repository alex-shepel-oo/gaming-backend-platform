import { Component, inject, signal } from '@angular/core';
import { GameSelectionService, GamesService, PublicGame } from 'shared';

@Component({
  selector: 'app-games',
  templateUrl: './games.html',
})
export class Games {
  private readonly gamesService = inject(GamesService);
  protected readonly gameSelection = inject(GameSelectionService);

  protected readonly games = signal<PublicGame[]>([]);

  constructor() {
    this.gamesService.listPublicGames().subscribe((games) => this.games.set(games));
  }

  protected select(game: PublicGame): void {
    this.gameSelection.select(game);
  }
}
