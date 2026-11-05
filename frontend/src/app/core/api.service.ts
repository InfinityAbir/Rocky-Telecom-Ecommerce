import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { ApiEnvelope } from './models';

/**
 * API origin. Development points at the Kestrel API (CORS allows http://localhost:4200);
 * production deployments serve the API from the same origin under /api/v1.
 */
const isBrowser = typeof window !== 'undefined';
const serverEnv = (key: string): string | undefined =>
  typeof process !== 'undefined' ? process.env?.[key] : undefined;

/** Browser on the Angular dev server (port 4200) talks to Kestrel directly; anywhere else the API shares the site's origin. */
const onDevServer = isBrowser && window.location.port === '4200';

/** Origin used in URLs the browser will load (API calls, uploaded images). Empty means "same origin". */
export const PUBLIC_ORIGIN = isBrowser
  ? onDevServer
    ? 'http://localhost:5129'
    : ''
  : (serverEnv('PUBLIC_API_ORIGIN') ?? 'http://localhost:5129');

/** Origin for requests made from here: server-side rendering reaches the API on its internal address. */
export const API_ORIGIN = isBrowser ? PUBLIC_ORIGIN : (serverEnv('API_INTERNAL_URL') ?? PUBLIC_ORIGIN);
export const API_BASE = `${API_ORIGIN}/api/v1`;

export const GUEST_TOKEN_KEY = 'rt_guest_token';

/** Query values; arrays are sent as repeated keys (`ram=8GB&ram=12GB`) which ASP.NET binds to lists. */
export type QueryValue = string | number | boolean | undefined | readonly string[];

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(path: string, params?: Record<string, QueryValue>): Observable<T> {
    return this.send<T>('GET', path, undefined, params);
  }

  post<T>(path: string, body: unknown = {}, headers?: Record<string, string>): Observable<T> {
    return this.send<T>('POST', path, body, undefined, headers);
  }

  put<T>(path: string, body: unknown = {}): Observable<T> {
    return this.send<T>('PUT', path, body);
  }

  delete<T>(path: string): Observable<T> {
    return this.send<T>('DELETE', path);
  }

  /** Multipart upload (images, CSV). The browser sets the multipart boundary, so no JSON content type. */
  upload<T>(path: string, form: FormData, params?: Record<string, QueryValue>): Observable<T> {
    let httpParams = new HttpParams();
    for (const [key, value] of Object.entries(params ?? {})) {
      if (value !== undefined && value !== null && value !== '' && !Array.isArray(value)) {
        httpParams = httpParams.set(key, String(value));
      }
    }

    return this.http
      .post<ApiEnvelope<T>>(`${API_BASE}${path}`, form, {
        params: httpParams,
        headers: this.headers().delete('Content-Type'),
        withCredentials: true
      })
      .pipe(
        map((envelope) => {
          if (!envelope || envelope.hasError) {
            throw new ApiError(envelope?.message ?? 'Something went wrong.', envelope?.errors);
          }

          return envelope.content;
        }),
        catchError((error: unknown) => throwError(() => toApiError(error)))
      );
  }

  /** Downloads a file with the signed-in user's token and hands it to the browser as a save dialog. */
  download(path: string, fallbackName: string, params?: Record<string, QueryValue>): Observable<void> {
    let httpParams = new HttpParams();
    for (const [key, value] of Object.entries(params ?? {})) {
      if (value !== undefined && value !== null && value !== '' && !Array.isArray(value)) {
        httpParams = httpParams.set(key, String(value));
      }
    }

    return this.http
      .get(`${API_BASE}${path}`, {
        params: httpParams,
        headers: this.headers().delete('Content-Type'),
        withCredentials: true,
        responseType: 'blob',
        observe: 'response'
      })
      .pipe(
        map((response) => {
          const disposition = response.headers.get('Content-Disposition') ?? '';
          const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
          const name = match ? decodeURIComponent(match[1]) : fallbackName;
          const url = URL.createObjectURL(response.body as Blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = name;
          link.click();
          setTimeout(() => URL.revokeObjectURL(url), 2000);
        }),
        catchError((error: unknown) => throwError(() => toApiError(error)))
      );
  }

  private send<T>(
    method: string,
    path: string,
    body?: unknown,
    params?: Record<string, QueryValue>,
    extraHeaders?: Record<string, string>
  ): Observable<T> {
    let httpParams = new HttpParams();

    for (const [key, value] of Object.entries(params ?? {})) {
      if (Array.isArray(value)) {
        for (const item of value as readonly string[]) {
          if (item !== '') {
            httpParams = httpParams.append(key, item);
          }
        }
      } else if (value !== undefined && value !== null && value !== '') {
        httpParams = httpParams.set(key, String(value));
      }
    }

    return this.http
      .request<ApiEnvelope<T>>(method, `${API_BASE}${path}`, {
        body,
        params: httpParams,
        headers: this.headers(extraHeaders),
        withCredentials: true
      })
      .pipe(
        map((envelope) => {
          if (!envelope || envelope.hasError) {
            throw new ApiError(envelope?.message ?? 'Something went wrong.', envelope?.errors);
          }

          return envelope.content;
        }),
        catchError((error: unknown) => throwError(() => toApiError(error)))
      );
  }

  private headers(extra?: Record<string, string>): HttpHeaders {
    let headers = new HttpHeaders({ 'Content-Type': 'application/json' });

    const token = this.readToken();
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }

    const guest = this.guestToken();
    if (guest) {
      headers = headers.set('X-Guest-Token', guest);
    }

    for (const [key, value] of Object.entries(extra ?? {})) {
      headers = headers.set(key, value);
    }

    return headers;
  }

  private readToken(): string | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage.getItem('rt_token');
    } catch {
      return null;
    }
  }

  private guestToken(): string | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage.getItem(GUEST_TOKEN_KEY);
    } catch {
      return null;
    }
  }
}

export class ApiError extends Error {
  constructor(message: string, public readonly fields?: Record<string, string[]>) {
    super(message);
  }
}

/** Normalises HTTP, network and domain errors into one message the UI can show. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return new ApiError('The shop is unreachable. Check your connection and try again.');
    }

    if (error.status === 429) {
      return new ApiError('Too many attempts. Please wait a minute and try again.');
    }

    const message =
      (error.error && typeof error.error === 'object' && 'message' in error.error
        ? String((error.error as { message?: string }).message)
        : null) ?? 'Something went wrong. Please try again.';

    const fields = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
    return new ApiError(message, fields);
  }

  return new ApiError(error instanceof Error ? error.message : 'Something went wrong. Please try again.');
}

/** Message for a toast: the first field-level validation message when there is one, else the summary. */
export function describeError(error: unknown): string {
  const apiError = toApiError(error);
  const first = Object.values(apiError.fields ?? {}).flat()[0];
  return first ?? apiError.message;
}

/**
 * The API serialises UTC DateTimes without a trailing "Z", which `new Date()` would read as local
 * time and shift by the viewer's offset. Treat a zone-less ISO string as UTC.
 */
export function parseUtc(iso: string): Date {
  return new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(iso) ? iso : `${iso}Z`);
}

/** Turns a stored image path into something the browser can render. */
export function mediaUrl(path: string | null | undefined): string {
  if (!path) {
    return '';
  }

  if (path.startsWith('http://') || path.startsWith('https://') || path.startsWith('data:')) {
    return path;
  }

  // Image URLs end up in the HTML the browser receives, so they must use the public origin, not the internal one.
  return `${PUBLIC_ORIGIN}${path.startsWith('/') ? '' : '/'}${path}`;
}
