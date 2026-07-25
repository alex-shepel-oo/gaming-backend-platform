export interface RegisterRequest {
  gameSlug: string;
  email: string;
  displayName: string;
  password: string;
}

export interface RegistrationAcceptedResponse {
  userId: string;
  email: string;
  verificationRequired: boolean;
  codeExpiresAt: string | null;
}

export interface ConfirmEmailRequest {
  email: string;
  code: string;
}

export interface ResendVerificationRequest {
  email: string;
  gameSlug?: string;
}

export interface RequestPasswordResetRequest {
  email: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}
