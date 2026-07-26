import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { IdentityAuthEndpoints, WEB_CLIENT_TYPE_HEADERS } from './identity-auth-endpoints';
import {
  ConfirmEmailRequest,
  RegisterRequest,
  RegistrationAcceptedResponse,
  RequestPasswordResetRequest,
  ResendVerificationRequest,
  ResetPasswordRequest,
} from './registration.models';
import { TokenStore } from './token-store';

export interface LoginCredentials {
  email: string;
  password: string;
  gameSlug?: string;
}

interface AccessTokenResponse {
  accessToken: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokenStore = inject(TokenStore);

  register(request: RegisterRequest): Observable<RegistrationAcceptedResponse> {
    return this.http.post<RegistrationAcceptedResponse>(IdentityAuthEndpoints.register, request);
  }

  confirmEmail(request: ConfirmEmailRequest): Observable<void> {
    return this.http.post<void>(IdentityAuthEndpoints.confirmEmail, request);
  }

  resendVerification(request: ResendVerificationRequest): Observable<void> {
    return this.http.post<void>(IdentityAuthEndpoints.resendVerification, request);
  }

  requestPasswordReset(request: RequestPasswordResetRequest): Observable<void> {
    return this.http.post<void>(IdentityAuthEndpoints.requestPasswordReset, request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(IdentityAuthEndpoints.resetPassword, request);
  }

  login(credentials: LoginCredentials): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.login, credentials, { headers: WEB_CLIENT_TYPE_HEADERS })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  selectGame(gameId: string): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.selectGame, { gameId }, { headers: WEB_CLIENT_TYPE_HEADERS })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  refresh(): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.refresh, null, { headers: WEB_CLIENT_TYPE_HEADERS })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(IdentityAuthEndpoints.logout, null, { headers: WEB_CLIENT_TYPE_HEADERS })
      .pipe(tap(() => this.tokenStore.clear()));
  }
}
