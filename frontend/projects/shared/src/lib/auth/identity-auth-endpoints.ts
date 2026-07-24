const IDENTITY_AUTH_BASE_PATH = '/api/identity/auth';

export const IdentityAuthEndpoints = {
  register: `${IDENTITY_AUTH_BASE_PATH}/register`,
  confirmEmail: `${IDENTITY_AUTH_BASE_PATH}/confirm-email`,
  resendVerification: `${IDENTITY_AUTH_BASE_PATH}/resend-verification`,
  login: `${IDENTITY_AUTH_BASE_PATH}/login`,
  refresh: `${IDENTITY_AUTH_BASE_PATH}/refresh`,
  logout: `${IDENTITY_AUTH_BASE_PATH}/logout`,
} as const;

export const WEB_CLIENT_TYPE_HEADERS = { 'X-Client-Type': 'web' } as const;

export const EMAIL_NOT_CONFIRMED_PROBLEM_TYPE =
  'https://gaming-backend-platform/problems/email-not-confirmed';
