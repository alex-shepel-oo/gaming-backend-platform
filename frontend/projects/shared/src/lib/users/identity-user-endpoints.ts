function withGameId(path: string, gameId?: string | null): string {
  if (!gameId) {
    return path;
  }

  return `${path}?${new URLSearchParams({ gameId }).toString()}`;
}

export const IdentityUserEndpoints = {
  list: (search: string | undefined, page: number, pageSize: number) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (search) params.set('search', search);
    return `/api/admin/identity/users?${params.toString()}`;
  },
  detail: (userId: string, gameId?: string | null) => withGameId(`/api/admin/identity/users/${userId}`, gameId),
  role: (userId: string) => `/api/admin/identity/users/${userId}/roles`,
  revokeSessions: (userId: string, gameId?: string) =>
    withGameId(`/api/admin/identity/users/${userId}/revoke-sessions`, gameId),
} as const;
