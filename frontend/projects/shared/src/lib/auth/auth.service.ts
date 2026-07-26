import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { CLIENT_TYPE } from './client-type';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';
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
  private readonly clientType = inject(CLIENT_TYPE);

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
      .post<AccessTokenResponse>(IdentityAuthEndpoints.login, credentials, { headers: this.clientTypeHeaders() })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  selectGame(gameId: string): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.selectGame, { gameId }, { headers: this.clientTypeHeaders() })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  refresh(): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.refresh, null, { headers: this.clientTypeHeaders() })
      .pipe(
        tap((response) => this.tokenStore.set(response.accessToken)),
        map(() => undefined),
      );
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(IdentityAuthEndpoints.logout, null, { headers: this.clientTypeHeaders() })
      .pipe(tap(() => this.tokenStore.clear()));
  }

  private clientTypeHeaders(): { 'X-Client-Type': string } {
    return { 'X-Client-Type': this.clientType };
  }
}
