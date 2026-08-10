import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule, MatSelectChange } from '@angular/material/select';
import { MatSidenavModule } from '@angular/material/sidenav';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import {
  colorFor,
  EmptyState,
  initialsOf,
  Loadable,
  RolePermissionsService,
  StatusPill,
  StatusPillVariant,
  TokenStore,
  UserDetail,
  UserManagementService,
  UserSummary,
} from 'shared';

// The PlatformRole enum names, case-sensitive, sent as-is to the roles endpoint.
const ROLES = ['Player', 'Moderator', 'Admin'] as const;
type Role = (typeof ROLES)[number];

// Purely a visual cue for scanning the table: Admin carries the widest blast
// radius if misused, Player carries none, so the pill escalates from neutral
// to warning rather than implying any real severity ranking.
const ROLE_VARIANTS: Record<string, StatusPillVariant> = {
  Admin: 'warning',
  Moderator: 'progress',
  Player: 'neutral',
};

const PAGE_SIZE = 20;

// The users list/detail endpoints are scoped server-side to the caller's own
// session: one game's roster when scope=game, every game's roster when
// scope=platform. A platform-scoped caller therefore sees rows spanning many
// games in the same paginated list, which is why each row carries its own
// gameId/gameSlug: selecting a row, assigning its role, or revoking its
// sessions must all act on that row's game, not the caller's own.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'admin-user-management',
  imports: [
    DatePipe,
    EmptyState,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSidenavModule,
    StatusPill,
  ],
  templateUrl: './user-management.html',
  styleUrls: ['./user-management.scss', './user-management-detail.scss', './user-management-responsive.scss'],
})
export class UserManagement {
  private readonly userManagementService = inject(UserManagementService);
  private readonly rolePermissionsService = inject(RolePermissionsService);
  protected readonly tokenStore = inject(TokenStore);

  protected readonly roles = ROLES;
  protected readonly pageSize = PAGE_SIZE;

  protected readonly scopeLabel = computed(() => (this.tokenStore.claims()?.gameId ? 'this game' : 'the platform'));

  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly users = signal<UserSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  private readonly listResource = new Loadable();
  protected readonly loading = this.listResource.loading;
  protected readonly loadError = this.listResource.error;

  protected readonly selectedUserId = signal<string | null>(null);
  protected readonly selectedUser = signal<UserDetail | null>(null);
  private readonly detailResource = new Loadable();
  protected readonly detailLoading = this.detailResource.loading;
  protected readonly detailError = this.detailResource.error;

  protected readonly selectedRole = signal<Role>('Player');
  protected readonly assigning = signal(false);
  protected readonly assignError = signal(false);

  protected readonly revoking = signal(false);
  protected readonly revokeError = signal(false);
  protected readonly revoked = signal(false);

  // Tracks which id was last copied (not just a bare flag) so the
  // check-mark swap is scoped to the row/field that was actually clicked.
  protected readonly copiedUserId = signal<string | null>(null);

  // One permission-set lookup per candidate role, fetched once per scope --
  // mirrors the backend's IRoleEscalationGuard.EnsureCanGrant loop exactly
  // (the caller must already hold every permission the target role would
  // carry in this scope), not an approximation based on role tiers.
  protected readonly rolePermissions = signal<Partial<Record<Role, string[]>>>({});

  // Matches the backend's Policies.Admin requirement on revoke-sessions.
  protected readonly canRevokeSessions = computed(() => this.tokenStore.claims()?.role === 'Admin');

  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput$ = new Subject<string>();

  constructor() {
    this.load();
    this.loadRolePermissions();

    // Debounced so a full name/email doesn't fire a paginated request per
    // keystroke. The signal itself still updates immediately so the input
    // box stays responsive.
    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page.set(1);
        this.load();
      });
  }

  protected avatarInitials(name: string): string {
    return initialsOf(name);
  }

  protected avatarColor(name: string): string {
    return colorFor(name);
  }

  protected roleVariant(role: string): StatusPillVariant {
    return ROLE_VARIANTS[role] ?? 'neutral';
  }

  protected copyUserId(id: string): void {
    navigator.clipboard
      .writeText(id)
      .then(() => {
        this.copiedUserId.set(id);

        const timeoutId = setTimeout(() => this.copiedUserId.set(null), 1500);
        this.destroyRef.onDestroy(() => clearTimeout(timeoutId));
      })
      .catch(() => {
        // Clipboard access can be denied by the browser (permissions, an
        // unfocused document); nothing useful to recover into, just don't
        // leave an unhandled rejection behind.
      });
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
    this.searchInput$.next(value);
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

  protected closeDetail(): void {
    this.selectedUserId.set(null);
    this.selectedUser.set(null);
    this.assignError.set(false);
    this.revokeError.set(false);
    this.revoked.set(false);
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
        // The assign response is UserRoleDto (no email/displayName), so refetch
        // the full record instead of patching the view from it directly.
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
    this.listResource.load(
      this.userManagementService.listUsers(this.search() || undefined, this.page(), PAGE_SIZE),
      (result) => {
        this.users.set(result.items);
        this.totalCount.set(result.totalCount);
      },
    );
  }

  private loadDetail(userId: string, gameId?: string | null): void {
    this.detailResource.load(this.userManagementService.getUser(userId, gameId), (user) => {
      this.selectedUser.set(user);
      this.selectedRole.set((user.role as Role) ?? 'Player');
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
