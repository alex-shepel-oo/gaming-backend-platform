export const IdentityGameEndpoints = {
  publicGames: '/api/identity/games/public',
  myGames: '/api/admin/identity/users/me/games',
  allGames: '/api/admin/identity/games',
  game: (id: string) => `/api/admin/identity/games/${id}`,
} as const;
