import { Injectable, signal } from '@angular/core';
import { PublicGame } from './games.service';

@Injectable({ providedIn: 'root' })
export class GameSelectionService {
  private readonly selectedGame = signal<PublicGame | null>(null);

  readonly selected = this.selectedGame.asReadonly();

  select(game: PublicGame): void {
    this.selectedGame.set(game);
  }

  clear(): void {
    this.selectedGame.set(null);
  }
}
