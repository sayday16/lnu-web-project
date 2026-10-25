import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, map, tap } from 'rxjs';
import { API_URL } from '../api.config';

const TOKEN_KEY = 'lnu_token';

interface TokenPayload {
  sub: string;
  email: string;
  exp: number;
}

interface LoginResponse {
  token: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));

  readonly isLoggedIn = computed(() => this.token() !== null);
  readonly email = computed(() => {
    const token = this.token();
    return token ? (decodePayload(token)?.email ?? null) : null;
  });

  getToken(): string | null {
    const token = this.token();
    if (token && isExpired(token)) {
      this.clearToken();
      return null;
    }
    return token;
  }

  register(email: string, password: string): Observable<void> {
    return this.http
      .post(`${API_URL}/auth/register`, { email, password })
      .pipe(map(() => undefined));
  }

  login(email: string, password: string): Observable<void> {
    return this.http.post<LoginResponse>(`${API_URL}/auth/login`, { email, password }).pipe(
      tap((response) => {
        localStorage.setItem(TOKEN_KEY, response.token);
        this.token.set(response.token);
      }),
      map(() => undefined),
    );
  }

  logout(): void {
    this.clearToken();
    this.router.navigate(['/login']);
  }

  private clearToken(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
  }
}

function decodePayload(token: string): TokenPayload | null {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(base64)) as TokenPayload;
  } catch {
    return null;
  }
}

function isExpired(token: string): boolean {
  const payload = decodePayload(token);
  return !payload || payload.exp * 1000 <= Date.now();
}
