import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { GamesService, Loadable, PublicGame, TokenStore } from 'shared';

// Deliberately narrower than GamesManagement: a caller here holds
// game.metadata.edit but not platform.games.manage, and the backend rejects
// anything beyond description/iconUrl from that caller, so this form doesn't
// even offer name/isActive/create.
// There's no "get one game by id" endpoint to back a single-game screen;
// listMyGames() already returns exactly the games the caller has a role on,
// which for a game-scoped Game-Admin is a one-element array.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'admin-my-game-metadata',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule],
  templateUrl: './my-game-metadata.html',
  styleUrl: './my-game-metadata.scss',
})
export class MyGameMetadata {
  private readonly gamesService = inject(GamesService);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly tokenStore = inject(TokenStore);

  private readonly gameResource = new Loadable();
  protected readonly loading = this.gameResource.loading;
  protected readonly loadError = this.gameResource.error;
  protected readonly noGame = signal(false);
  protected readonly game = signal<PublicGame | null>(null);

  protected readonly editForm = this.formBuilder.nonNullable.group({
    description: [''],
    iconUrl: [''],
  });

  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);
  protected readonly saved = signal(false);

  constructor() {
    if (!this.tokenStore.claims()?.gameId) {
      this.gameResource.loading.set(false);
      this.noGame.set(true);
      return;
    }

    this.load();
  }

  protected submitEdit(): void {
    const game = this.game();

    if (!game || this.editForm.invalid) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(false);
    this.saved.set(false);

    this.gamesService.updateGame(game.id, this.editForm.getRawValue()).subscribe({
      next: (updated) => {
        this.game.set(updated);
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set(true);
      },
    });
  }

  private load(): void {
    this.noGame.set(false);

    this.gameResource.load(this.gamesService.listMyGames(), (games) => {
      const game = games[0];

      if (!game) {
        this.noGame.set(true);
        return;
      }

      this.game.set(game);
      this.editForm.setValue({
        description: game.description ?? '',
        iconUrl: game.iconUrl ?? '',
      });
    });
  }
}
