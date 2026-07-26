export const IdentityUserEndpoints = {
  list: (search: string | undefined, page: number, pageSize: number) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (search) params.set('search', search);
    return `/api/admin/identity/users?${params.toString()}`;
  },
  detail: (userId: string) => `/api/admin/identity/users/${userId}`,
  role: (userId: string) => `/api/admin/identity/users/${userId}/roles`,
  revokeSessions: (userId: string, gameId?: string) =>
    `/api/admin/identity/users/${userId}/revoke-sessions${gameId ? `?gameId=${gameId}` : ''}`,
} as const;
