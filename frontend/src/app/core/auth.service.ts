import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from './api.service';
import { AuthResponse, AuthUser } from './models';

const TOKEN_KEY = 'rt_token';
const USER_KEY = 'rt_user';

/**
 * Holds the access token (Authorization header) and the decoded user profile.
 * The refresh token is an httpOnly cookie owned by the API — never readable here.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);

  readonly user = signal<AuthUser | null>(this.readUser());
  readonly token = signal<string | null>(this.readValue(TOKEN_KEY));
  readonly isSignedIn = computed(() => this.user() !== null);
  readonly isAdmin = computed(() => {
    const roles = this.user()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('Staff') || roles.includes('Editor');
  });
  readonly isAdminOnly = computed(() => (this.user()?.roles ?? []).includes('Admin'));

  login(phone: string, password: string): Observable<AuthResponse> {
    return this.api
      .post<AuthResponse>('/auth/login', { phone, password })
      .pipe(tap((response) => this.store(response)));
  }

  register(name: string, phone: string, password: string, email?: string): Observable<AuthResponse> {
    return this.api
      .post<AuthResponse>('/auth/register', { name, phone, password, email: email || null })
      .pipe(tap((response) => this.store(response)));
  }

  logout(): void {
    this.api.post('/auth/logout').subscribe({ error: () => undefined });
    this.clear();
  }

  /** Refresh the access token from the httpOnly cookie (called by the interceptor on 401). */
  refresh(): Observable<AuthResponse> {
    return this.api
      .post<AuthResponse>('/auth/refresh')
      .pipe(tap((response) => this.store(response)));
  }

  clear(): void {
    this.user.set(null);
    this.token.set(null);

    try {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(USER_KEY);
    } catch {
      // Storage unavailable (private mode / SSR) — the in-memory session still clears.
    }
  }

  private store(response: AuthResponse): void {
    this.user.set(response.user);
    this.token.set(response.accessToken);

    try {
      localStorage.setItem(TOKEN_KEY, response.accessToken);
      localStorage.setItem(USER_KEY, JSON.stringify(response.user));
    } catch {
      // Non-fatal: the session lives in memory for this tab.
    }
  }

  private readUser(): AuthUser | null {
    const raw = this.readValue(USER_KEY);

    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthUser;
    } catch {
      return null;
    }
  }

  private readValue(key: string): string | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage.getItem(key);
    } catch {
      return null;
    }
  }
}
