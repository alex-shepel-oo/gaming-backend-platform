const IDENTITY_AUTH_BASE_PATH = '/api/identity/auth';

export const IdentityAuthEndpoints = {
  login: `${IDENTITY_AUTH_BASE_PATH}/login`,
  refresh: `${IDENTITY_AUTH_BASE_PATH}/refresh`,
  logout: `${IDENTITY_AUTH_BASE_PATH}/logout`,
} as const;

export const WEB_CLIENT_TYPE_HEADERS = { 'X-Client-Type': 'web' } as const;
