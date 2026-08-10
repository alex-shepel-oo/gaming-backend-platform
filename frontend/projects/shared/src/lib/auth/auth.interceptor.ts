import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { IdentityAuthEndpoints } from './identity-auth-endpoints';
import { TokenRefreshCoordinator } from './token-refresh-coordinator';
import { TokenStore } from './token-store';

function withAuth(req: HttpRequest<unknown>, accessToken: string | null): HttpRequest<unknown> {
  const withCredentials = req.clone({ withCredentials: true });

  return accessToken
    ? withCredentials.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
    : withCredentials;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStore = inject(TokenStore);
  const refreshCoordinator = inject(TokenRefreshCoordinator);

  const authorizedReq = withAuth(req, tokenStore.read());

  if (authorizedReq.url.endsWith(IdentityAuthEndpoints.refresh)) {
    return next(authorizedReq);
  }

  return next(authorizedReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      // Shared across every request that hits a 401 at once, see
      // TokenRefreshCoordinator for why that matters.
      return refreshCoordinator.refresh().pipe(
        switchMap(() => next(withAuth(req, tokenStore.read()))),
        catchError(() => {
          tokenStore.clear();

          return throwError(() => error);
        }),
      );
    }),
  );
};
