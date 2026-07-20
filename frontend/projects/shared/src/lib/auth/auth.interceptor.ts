import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { IdentityAuthEndpoints, WEB_CLIENT_TYPE_HEADERS } from './identity-auth-endpoints';
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

  const authorizedReq = withAuth(req, tokenStore.read());

  if (authorizedReq.url.endsWith(IdentityAuthEndpoints.refresh)) {
    return next(authorizedReq);
  }

  return next(authorizedReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return http.post<RefreshResponse>(IdentityAuthEndpoints.refresh, null, { headers: WEB_CLIENT_TYPE_HEADERS }).pipe(
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
