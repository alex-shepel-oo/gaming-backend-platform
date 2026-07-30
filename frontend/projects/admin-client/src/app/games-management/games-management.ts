import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTableModule } from '@angular/material/table';
import { Game, GamesService } from 'shared';

// Platform-section, permission-gated at the route level (platform.games.manage).
// This screen only reads/writes through the existing games CRUD endpoints --
// no new backend surface, no client-side re-implementation of any rule the
// backend already enforces (e.g. duplicate slugs are still rejected server-side;
// this component just surfaces whatever error comes back).
@Component({
  selector: 'admin-games-management',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSidenavModule,
    MatTableModule,
  ],
  templateUrl: './games-management.html',
  styleUrl: './games-management.scss',
})
export class GamesManagement {
  private readonly gamesService = inject(GamesService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly displayedColumns = ['slug', 'name', 'isActive', 'actions'];

  protected readonly games = signal<Game[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly createForm = this.formBuilder.nonNullable.group({
    slug: ['', Validators.required],
    name: ['', Validators.required],
  });
  protected readonly creating = signal(false);
  protected readonly createError = signal(false);

  protected readonly editingId = signal<string | null>(null);
  protected readonly editingGame = computed(() => this.games().find((game) => game.id === this.editingId()) ?? null);
  protected readonly editForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    isActive: [true],
    description: [''],
    iconUrl: [''],
  });
  protected readonly saveError = signal(false);

  constructor() {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.gamesService.listAllGames().subscribe({
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

    this.gamesService.updateGame(id, this.editForm.getRawValue()).subscribe({
      next: (updated) => {
        this.games.update((games) => games.map((game) => (game.id === id ? updated : game)));
        this.editingId.set(null);
      },
      error: () => {
        this.saveError.set(true);
      },
    });
  }
}
