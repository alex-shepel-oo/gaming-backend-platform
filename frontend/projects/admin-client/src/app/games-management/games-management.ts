import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { EmptyState, Game, GamesService, Loadable, StatusPill } from 'shared';

// Platform-section, permission-gated at the route level (platform.games.manage).
// This screen only reads/writes through the existing games CRUD endpoints --
// no new backend surface, no client-side re-implementation of any rule the
// backend already enforces (e.g. duplicate slugs are still rejected server-side;
// this component just surfaces whatever error comes back).
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'admin-games-management',
  imports: [
    ReactiveFormsModule,
    EmptyState,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSidenavModule,
    MatSlideToggleModule,
    StatusPill,
  ],
  templateUrl: './games-management.html',
  styleUrl: './games-management.scss',
})
export class GamesManagement {
  private readonly gamesService = inject(GamesService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly games = signal<Game[]>([]);
  private readonly gamesResource = new Loadable();
  protected readonly loading = this.gamesResource.loading;
  protected readonly loadError = this.gamesResource.error;

  protected readonly createForm = this.formBuilder.nonNullable.group({
    slug: ['', [Validators.required, Validators.maxLength(100)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });
  protected readonly creating = signal(false);
  protected readonly createError = signal(false);

  protected readonly editingId = signal<string | null>(null);
  protected readonly editingGame = computed(() => this.games().find((game) => game.id === this.editingId()) ?? null);
  protected readonly editForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    isActive: [true],
    description: ['', Validators.maxLength(2000)],
    iconUrl: [''],
  });
  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);

  // Tracks whichever row a quick action (toggle/delete) is currently in
  // flight for, so only that row's buttons disable and the error message
  // targets the right game.
  protected readonly busyId = signal<string | null>(null);
  protected readonly rowActionErrorId = signal<string | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    this.gamesResource.load(this.gamesService.listAllGames(), (games) => this.games.set(games));
  }

  protected create(): void {
    if (this.createForm.invalid) {
      return;
    }

    this.creating.set(true);
    this.createError.set(false);

    this.gamesService.createGame(this.createForm.getRawValue()).subscribe({
      next: (game) => {
        this.games.update((games) => [...games, game]);
        this.creating.set(false);
        this.createForm.reset({ slug: '', name: '' });
      },
      error: () => {
        this.creating.set(false);
        this.createError.set(true);
      },
    });
  }

  protected startEdit(game: Game): void {
    this.editingId.set(game.id);
    this.saveError.set(false);
    this.editForm.setValue({
      name: game.name,
      isActive: game.isActive,
      description: game.description ?? '',
      iconUrl: game.iconUrl ?? '',
    });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected saveEdit(id: string): void {
    if (this.editForm.invalid) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(false);

    this.gamesService.updateGame(id, this.editForm.getRawValue()).subscribe({
      next: (updated) => {
        this.games.update((games) => games.map((game) => (game.id === id ? updated : game)));
        this.saving.set(false);
        this.editingId.set(null);
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set(true);
      },
    });
  }

  // A one-click alternative to opening the edit sidenav just to flip one
  // toggle. Reversible, so unlike deleteGame() this needs no confirmation.
  protected toggleActive(game: Game): void {
    this.busyId.set(game.id);
    this.rowActionErrorId.set(null);

    this.gamesService.updateGame(game.id, { isActive: !game.isActive }).subscribe({
      next: (updated) => {
        this.games.update((games) => games.map((g) => (g.id === game.id ? updated : g)));
        this.busyId.set(null);
      },
      error: () => {
        this.busyId.set(null);
        this.rowActionErrorId.set(game.id);
      },
    });
  }

  // Backend rejects this outright while the game is still active. Deactivate
  // is the reversible half of "remove a game," this is the irreversible half.
  protected deleteGame(game: Game): void {
    if (game.isActive) {
      return;
    }

    const confirmed = confirm(
      `Permanently delete "${game.name}"? This can't be undone, and any of its currencies, balances, ` +
        `or conversions already recorded elsewhere won't be cleaned up.`,
    );

    if (!confirmed) {
      return;
    }

    this.busyId.set(game.id);
    this.rowActionErrorId.set(null);

    this.gamesService.deleteGame(game.id).subscribe({
      next: () => {
        this.games.update((games) => games.filter((g) => g.id !== game.id));
        this.busyId.set(null);
      },
      error: () => {
        this.busyId.set(null);
        this.rowActionErrorId.set(game.id);
      },
    });
  }
}
