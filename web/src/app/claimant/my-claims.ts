import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, interval, startWith, switchMap } from 'rxjs';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { CurrentUserService } from '../core/current-user.service';
import { ClaimSummary } from '../core/models';
import { StatusBadge } from '../shared/status-badge';

@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, DatePipe, DecimalPipe, StatusBadge],
  template: `
    <div class="page-header">
      <h1>My claims</h1>
      <a class="btn" routerLink="/claims/new">Report an incident</a>
    </div>

    @if (error()) {
      <p class="alert alert-error" role="alert">{{ error() }}</p>
    }

    @if (claims(); as claims) {
      @if (claims.length === 0) {
        <p class="card muted">You haven't reported any incidents yet.</p>
      } @else {
        <div class="table-wrap card">
          <table>
            <thead>
              <tr><th>Claim</th><th>Type</th><th>Status</th><th class="num">Claimed</th><th>Last update</th></tr>
            </thead>
            <tbody>
              @for (claim of claims; track claim.id) {
                <tr>
                  <td><a [routerLink]="['/claims', claim.id]">{{ claim.number }}</a></td>
                  <td>{{ claim.type }}</td>
                  <td><app-status-badge [status]="claim.status" /></td>
                  <td class="num">{{ claim.currency }} {{ claim.claimedAmount | number: '1.2-2' }}</td>
                  <td>{{ claim.updatedAt | date: 'd MMM y, HH:mm' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class MyClaims {
  private readonly api = inject(ClaimsApi);
  protected readonly claims = signal<ClaimSummary[] | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Reload when the user changes, then every 10 seconds while this page is open.
    toObservable(inject(CurrentUserService).userId)
      .pipe(
        switchMap(() => interval(10_000).pipe(startWith(0))),
        switchMap(() => this.api.list().pipe(catchError((e) => (this.error.set(problemMessage(e)), EMPTY)))),
        takeUntilDestroyed(),
      )
      .subscribe((claims) => {
        this.error.set(null);
        this.claims.set(claims);
      });
  }
}
