export const IdentityRoleEndpoints = {
  permissionCatalog: '/api/admin/identity/permissions',
  rolePermissions: (role: string, gameId?: string) =>
    gameId
      ? `/api/admin/identity/roles/${role}/permissions?${new URLSearchParams({ gameId }).toString()}`
      : `/api/admin/identity/roles/${role}/permissions`,
} as const;
