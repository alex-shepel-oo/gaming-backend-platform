import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { IdentityAuthEndpoints, WEB_CLIENT_TYPE_HEADERS } from './identity-auth-endpoints';
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

  login(credentials: LoginCredentials): Observable<void> {
    return this.http
      .post<AccessTokenResponse>(IdentityAuthEndpoints.login, credentials, { headers: WEB_CLIENT_TYPE_HEADERS })
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
