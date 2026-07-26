export const IdentityRoleEndpoints = {
  permissionCatalog: '/api/admin/identity/permissions',
  rolePermissions: (role: string, gameId?: string) =>
    `/api/admin/identity/roles/${role}/permissions${gameId ? `?gameId=${gameId}` : ''}`,
} as const;
