import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule, MatCheckboxChange } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule, MatSelectChange } from '@angular/material/select';
import { Game, GamesService, RolePermissionsService, TokenStore } from 'shared';

// The PlatformRole enum names, case-sensitive -- sent as-is in the roles/{role}/permissions URL.
const ROLES = ['Player', 'Moderator', 'Admin'] as const;
type Role = (typeof ROLES)[number];

// "Platform-wide" is modeled as gameId: null, mirroring how the backend
// itself treats RolePermission.GameId (null == the platform-wide template,
// a game id == that game's own template) -- not a separate concept from the
// per-game rows, just the same field with no game selected.
const PLATFORM_WIDE = 'platform-wide';

@Component({
  selector: 'admin-role-permissions-editor',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './role-permissions-editor.html',
  styleUrl: './role-permissions-editor.scss',
})
export class RolePermissionsEditor {
  private readonly rolePermissionsService = inject(RolePermissionsService);
  private readonly gamesService = inject(GamesService);
  private readonly tokenStore = inject(TokenStore);

  protected readonly roles = ROLES;
  protected readonly platformWide = PLATFORM_WIDE;

  protected readonly selectedRole = signal<Role>('Player');
  protected readonly selectedGameOption = signal<string>(PLATFORM_WIDE);

  protected readonly games = signal<Game[]>([]);
  protected readonly catalog = signal<string[]>([]);
  protected readonly granted = signal<Set<string>>(new Set());

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly grantsLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);

  // Anti-escalation UX only: the backend's IRoleEscalationGuard is the real
  // boundary and rejects a save that grants a permission the caller doesn't
  // hold themselves regardless of this. Disabling (not hiding) these
  // checkboxes just means the caller isn't shown a control that would fail
  // anyway -- they still see what the role currently has.
  protected readonly callerPermissions = computed(() => new Set(this.tokenStore.claims()?.permissions ?? []));

  private get selectedGameId(): string | undefined {
    const option = this.selectedGameOption();

    return option === PLATFORM_WIDE ? undefined : option;
  }

  constructor() {
    this.rolePermissionsService.getPermissionCatalog().subscribe({
      next: (catalog) => {
        this.catalog.set(catalog);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });

    this.gamesService.listAllGames().subscribe((games) => this.games.set(games));

    this.loadGrants();
  }

  protected onRoleChange(change: MatSelectChange): void {
    this.selectedRole.set(change.value as Role);
    this.loadGrants();
  }

  protected onGameOptionChange(change: MatSelectChange): void {
    this.selectedGameOption.set(change.value as string);
    this.loadGrants();
  }

  protected isGranted(permission: string): boolean {
    return this.granted().has(permission);
  }

  protected isDisabled(permission: string): boolean {
    return !this.callerPermissions().has(permission);
  }

  protected onToggle(permission: string, change: MatCheckboxChange): void {
    this.granted.update((current) => {
      const next = new Set(current);

      if (change.checked) {
        next.add(permission);
      } else {
        next.delete(permission);
      }

      return next;
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.saveError.set(false);

    this.rolePermissionsService
      .updateRolePermissions(this.selectedRole(), Array.from(this.granted()), this.selectedGameId)
      .subscribe({
        next: (permissions) => {
          this.granted.set(new Set(permissions));
          this.saving.set(false);
        },
        error: () => {
          this.saving.set(false);
          this.saveError.set(true);
        },
      });
  }

  private loadGrants(): void {
    this.grantsLoading.set(true);

    this.rolePermissionsService.getRolePermissions(this.selectedRole(), this.selectedGameId).subscribe({
      next: (permissions) => {
        this.granted.set(new Set(permissions));
        this.grantsLoading.set(false);
      },
      error: () => {
        this.grantsLoading.set(false);
        this.loadError.set(true);
      },
    });
  }
}
