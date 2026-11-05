import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  HttpErrorResponse,
  HttpInterceptorFn
} from '@angular/common/http';
import { catchError, switchMap, throwError } from 'rxjs';
import { inject } from '@angular/core';

import { routes } from './app.routes';
import { provideClientHydration, withHttpTransferCacheOptions } from '@angular/platform-browser';
import { AuthService } from './core/auth.service';

/** One silent refresh attempt on a 401, then let the caller see the failure. */
const refreshInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const isAuthCall = request.url.includes('/auth/');

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !isAuthCall &&
        auth.isSignedIn()
      ) {
        return auth.refresh().pipe(
          switchMap(() => next(request)),
          catchError((retryError) => {
            auth.clear();
            return throwError(() => retryError);
          })
        );
      }

      return throwError(() => error);
    })
  );
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' })
    ),
    // Server-rendered responses are replayed in the browser, so only public catalogue reads may be shared:
    // the server has no guest token or login, and would otherwise hand every visitor an empty cart.
    provideClientHydration(
      withHttpTransferCacheOptions({
        filter: (request) => !/\/(cart|me|orders|order|track|account|admin|auth|wishlist|pre-orders)(\/|\?|$)/.test(request.url)
      })
    ),
    provideHttpClient(withFetch(), withInterceptors([refreshInterceptor]))
  ]
};
