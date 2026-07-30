import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule, MatSelectChange } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { RolePermissionsService, TokenStore, UserDetail, UserManagementService, UserSummary } from 'shared';

// The PlatformRole enum names, case-sensitive -- sent as-is to the roles endpoint.
const ROLES = ['Player', 'Moderator', 'Admin'] as const;
type Role = (typeof ROLES)[number];

const PAGE_SIZE = 20;

// The users list/detail endpoints are scoped server-side to the caller's own
// session: one game's roster when scope=game, every game's roster when
// scope=platform. A platform-scoped caller therefore sees rows spanning many
// games in the same paginated list, which is why each row carries its own
// gameId/gameSlug -- selecting a row, assigning its role, or revoking its
// sessions must all act on that row's game, not the caller's own.
@Component({
  selector: 'admin-user-management',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './user-management.html',
  styleUrl: './user-management.scss',
})
export class UserManagement {
  private readonly userManagementService = inject(UserManagementService);
  private readonly rolePermissionsService = inject(RolePermissionsService);
  protected readonly tokenStore = inject(TokenStore);

  protected readonly roles = ROLES;
  protected readonly pageSize = PAGE_SIZE;
  protected readonly displayedColumns = ['email', 'displayName', 'role', 'game', 'createdAt', 'lastLoginAt'];

  protected readonly scopeLabel = computed(() => (this.tokenStore.claims()?.gameId ? 'this game' : 'the platform'));

  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly users = signal<UserSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly selectedUserId = signal<string | null>(null);
  protected readonly selectedUser = signal<UserDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal(false);

  protected readonly selectedRole = signal<Role>('Player');
  protected readonly assigning = signal(false);
  protected readonly assignError = signal(false);

  protected readonly revoking = signal(false);
  protected readonly revokeError = signal(false);
  protected readonly revoked = signal(false);

  // One permission-set lookup per candidate role, fetched once per scope --
  // mirrors the backend's IRoleEscalationGuard.EnsureCanGrant loop exactly
  // (the caller must already hold every permission the target role would
  // carry in this scope), not an approximation based on role tiers.
  protected readonly rolePermissions = signal<Partial<Record<Role, string[]>>>({});

  // Matches the backend's Policies.Admin requirement on revoke-sessions.
  protected readonly canRevokeSessions = computed(() => this.tokenStore.claims()?.role === 'Admin');

  constructor() {
    this.load();
    this.loadRolePermissions();
  }

  protected isRoleDisabled(role: Role): boolean {
    const required = this.rolePermissions()[role];

    if (required === undefined) {
      return true;
    }

    const held = new Set(this.tokenStore.claims()?.permissions ?? []);

    return !required.every((permission) => held.has(permission));
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    this.load();
  }

  protected nextPage(): void {
    this.page.update((page) => page + 1);
    this.load();
  }

  protected previousPage(): void {
    this.page.update((page) => Math.max(1, page - 1));
    this.load();
  }

  protected selectUser(user: UserSummary): void {
    this.selectedUserId.set(user.id);
    this.selectedUser.set(null);
    this.assignError.set(false);
    this.revokeError.set(false);
    this.revoked.set(false);
    this.loadDetail(user.id, user.gameId);
  }

  protected onRoleChange(change: MatSelectChange): void {
    this.selectedRole.set(change.value as Role);
  }

  protected assignRole(): void {
    const userId = this.selectedUserId();

    if (!userId) {
      return;
    }

    this.assigning.set(true);
    this.assignError.set(false);

    const gameId = this.selectedUser()?.gameId ?? undefined;

    this.userManagementService.assignRole(userId, gameId, this.selectedRole()).subscribe({
      next: () => {
        this.assigning.set(false);
        // The assign response is UserRoleDto (no email/displayName) -- refetch
        // the full record so the detail view doesn't go stale or lose fields.
        this.loadDetail(userId, gameId);
      },
      error: () => {
        this.assigning.set(false);
        this.assignError.set(true);
      },
    });
  }

  protected revokeSessions(): void {
    const userId = this.selectedUserId();

    if (!userId) {
      return;
    }

    this.revoking.set(true);
    this.revokeError.set(false);
    this.revoked.set(false);

    this.userManagementService.revokeSessions(userId, this.selectedUser()?.gameId ?? undefined).subscribe({
      next: () => {
        this.revoking.set(false);
        this.revoked.set(true);
      },
      error: () => {
        this.revoking.set(false);
        this.revokeError.set(true);
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.userManagementService.listUsers(this.search() || undefined, this.page(), PAGE_SIZE).subscribe({
      next: (result) => {
        this.users.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  private loadDetail(userId: string, gameId?: string | null): void {
    this.detailLoading.set(true);
    this.detailError.set(false);

    this.userManagementService.getUser(userId, gameId).subscribe({
      next: (user) => {
        this.selectedUser.set(user);
        this.selectedRole.set((user.role as Role) ?? 'Player');
        this.detailLoading.set(false);
      },
      error: () => {
        this.detailLoading.set(false);
        this.detailError.set(true);
      },
    });
  }

  private loadRolePermissions(): void {
    const gameId = this.tokenStore.claims()?.gameId ?? undefined;

    for (const role of ROLES) {
      this.rolePermissionsService
        .getRolePermissions(role, gameId)
        .subscribe((permissions) => this.rolePermissions.update((current) => ({ ...current, [role]: permissions })));
    }
  }
}
