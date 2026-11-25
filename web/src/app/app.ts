import { HttpClient } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CurrentUserService, homeFor } from './core/current-user.service';
import { Role, User } from './core/models';

const ROLE_GROUPS: { role: Role; label: string }[] = [
  { role: 'Claimant', label: 'Claimants' },
  { role: 'Officer', label: 'Claims officers' },
  { role: 'Manager', label: 'Managers' },
];

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  private readonly router = inject(Router);
  protected readonly currentUser = inject(CurrentUserService);

  constructor() {
    // Load the seeded users for the switcher (open endpoint: no user is needed yet).
    inject(HttpClient)
      .get<User[]>('/api/users')
      .pipe(takeUntilDestroyed())
      .subscribe((users) => this.currentUser.users.set(users));
  }

  protected readonly groups = computed(() =>
    ROLE_GROUPS.map((g) => ({ ...g, users: this.currentUser.users().filter((u) => u.role === g.role) })),
  );

  protected choose(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    const user = this.currentUser.users().find((u) => u.id === id);
    if (!user) return;
    this.currentUser.select(id);
    this.router.navigateByUrl(homeFor(user.role));
  }
}
