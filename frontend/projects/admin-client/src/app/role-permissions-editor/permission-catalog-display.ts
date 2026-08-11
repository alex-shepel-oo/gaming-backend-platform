// Hand-authored client-side grouping for the permission catalog returned by
// GET /api/identity/permissions -- that endpoint returns a flat, uncategorized
// list (backend/IdentityService/Auth/Permissions.cs has no notion of category),
// so the grouping and icon choice below have no server-side source of truth.
// Any permission the catalog returns that isn't listed here still renders --
// see permissionCategoryOf's fallback -- it just lands in its own "Other" group
// instead of silently disappearing.
export interface PermissionCategory {
  readonly name: string;
  readonly icon: string;
  readonly permissions: readonly string[];
}

export const PERMISSION_CATEGORIES: readonly PermissionCategory[] = [
  {
    name: 'Games',
    icon: 'sports_esports',
    permissions: ['platform.games.manage', 'game.metadata.edit'],
  },
  {
    name: 'Currency & Balance',
    icon: 'payments',
    permissions: [
      'platform.currency.manage',
      'platform.balance.adjust',
      'game.currency.manage',
      'game.balance.adjust',
    ],
  },
  {
    name: 'Roles & Access',
    icon: 'security',
    permissions: ['platform.roles.manage', 'game.roles.manage'],
  },
  {
    name: 'Users',
    icon: 'group',
    permissions: ['platform.users.read', 'game.players.moderate'],
  },
];

const OTHER_CATEGORY: PermissionCategory = { name: 'Other', icon: 'more_horiz', permissions: [] };

export function groupPermissionsByCategory(
  catalog: readonly string[],
): { category: PermissionCategory; permissions: string[] }[] {
  const categorized = PERMISSION_CATEGORIES.map((category) => ({
    category,
    permissions: category.permissions.filter((permission) => catalog.includes(permission)),
  })).filter((group) => group.permissions.length > 0);

  const known = new Set(PERMISSION_CATEGORIES.flatMap((category) => category.permissions));
  const uncategorized = catalog.filter((permission) => !known.has(permission));

  if (uncategorized.length > 0) {
    categorized.push({ category: OTHER_CATEGORY, permissions: uncategorized });
  }

  return categorized;
}
