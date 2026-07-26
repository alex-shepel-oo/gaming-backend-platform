import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { CLIENT_TYPE } from './client-type';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';
import { TokenStore } from './token-store';

interface RefreshResponse {
  accessToken: string;
}

function withAuth(req: HttpRequest<unknown>, accessToken: string | null): HttpRequest<unknown> {
  const withCredentials = req.clone({ withCredentials: true });

  return accessToken
    ? withCredentials.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
    : withCredentials;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStore = inject(TokenStore);
  const http = inject(HttpClient);
  const clientType = inject(CLIENT_TYPE);

  const authorizedReq = withAuth(req, tokenStore.read());

  if (authorizedReq.url.endsWith(IdentityAuthEndpoints.refresh)) {
    return next(authorizedReq);
  }

  return next(authorizedReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return http.post<RefreshResponse>(IdentityAuthEndpoints.refresh, null, { headers: { 'X-Client-Type': clientType } }).pipe(
        switchMap((response) => {
          tokenStore.set(response.accessToken);

          return next(withAuth(req, response.accessToken));
        }),
        catchError(() => {
          tokenStore.clear();

          return throwError(() => error);
        }),
      );
    }),
  );
};
