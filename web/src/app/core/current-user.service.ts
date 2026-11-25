import { computed, Injectable, signal } from '@angular/core';
import { Role, User } from './models';

const STORAGE_KEY = 'claimdesk.userId';

/**
 * Who the app is acting as. This is the role switcher, a stand-in for real login (DESIGN.md §2).
 * The chosen id is sent to the APIs as X-User-Id by userIdInterceptor.
 *
 * Deliberately has no HttpClient dependency: the interceptor injects this service, so if this service
 * made HTTP calls itself, Angular would hit a circular dependency (NG0200). The App component loads the users.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  readonly users = signal<User[]>([]);
  readonly userId = signal<string | null>(localStorage.getItem(STORAGE_KEY));
  readonly user = computed(() => this.users().find((u) => u.id === this.userId()) ?? null);

  select(id: string): void {
    localStorage.setItem(STORAGE_KEY, id);
    this.userId.set(id);
  }
}

/** Where each role lands after switching user. */
export function homeFor(role: Role): string {
  return role === 'Claimant' ? '/claims' : role === 'Officer' ? '/work' : '/dashboard';
}
