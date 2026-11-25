import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, interval, startWith, Subject, switchMap } from 'rxjs';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { CurrentUserService } from '../core/current-user.service';
import { ClaimSummary } from '../core/models';
import { StatusBadge } from '../shared/status-badge';

/** An officer's queue: unassigned claims to pick up, and the claims they own. */
@Component({
  selector: 'app-work-queue',
  imports: [DatePipe, DecimalPipe, RouterLink, StatusBadge],
  template: `
    <div class="page-header"><h1>Work queue</h1></div>

    @if (error()) {
      <p class="alert alert-error" role="alert">{{ error() }}</p>
    }

    <div class="stack">
      <section aria-labelledby="unassigned-heading">
        <h2 id="unassigned-heading">Unassigned ({{ unassigned().length }})</h2>
        @if (unassigned().length === 0) {
          <p class="card muted">Nothing waiting to be picked up.</p>
        } @else {
          <div class="table-wrap card">
            <table>
              <thead>
                <tr><th>Claim</th><th>Claimant</th><th>Type</th><th class="num">Claimed</th><th>Submitted</th><th><span class="sr-only">Action</span></th></tr>
              </thead>
              <tbody>
                @for (claim of unassigned(); track claim.id) {
                  <tr>
                    <td><a [routerLink]="['/work', claim.id]">{{ claim.number }}</a></td>
                    <td>{{ claim.claimantName }} ({{ claim.market }})</td>
                    <td>{{ claim.type }}</td>
                    <td class="num">{{ claim.currency }} {{ claim.claimedAmount | number: '1.2-2' }}</td>
                    <td>{{ claim.createdAt | date: 'd MMM, HH:mm' }}</td>
                    <td><button class="btn" type="button" (click)="pickUp(claim)" [disabled]="busyId() === claim.id">Pick up</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>

      <section aria-labelledby="mine-heading">
        <h2 id="mine-heading">My claims ({{ mine().length }})</h2>
        @if (mine().length === 0) {
          <p class="card muted">You have no claims assigned.</p>
        } @else {
          <div class="table-wrap card">
            <table>
              <thead>
                <tr><th>Claim</th><th>Claimant</th><th>Status</th><th class="num">Reserve</th><th>Last update</th></tr>
              </thead>
              <tbody>
                @for (claim of mine(); track claim.id) {
                  <tr>
                    <td><a [routerLink]="['/work', claim.id]">{{ claim.number }}</a></td>
                    <td>{{ claim.claimantName }} ({{ claim.market }})</td>
                    <td><app-status-badge [status]="claim.status" /></td>
                    <td class="num">{{ claim.currency }} {{ claim.reserve | number: '1.2-2' }}</td>
                    <td>{{ claim.updatedAt | date: 'd MMM, HH:mm' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `,
})
export class WorkQueue {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);
  private readonly currentUser = inject(CurrentUserService);
  private readonly refresh = new Subject<void>();

  private readonly claims = signal<ClaimSummary[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);

  protected readonly unassigned = computed(() => this.claims().filter((c) => c.status === 'Submitted' && !c.assignedOfficerId));
  protected readonly mine = computed(() => this.claims().filter((c) => c.assignedOfficerId === this.currentUser.userId()));

  constructor() {
    // Reload when the user changes, every 10 seconds, and on demand (after a failed pick-up).
    toObservable(this.currentUser.userId)
      .pipe(
        switchMap(() => interval(10_000).pipe(startWith(0))),
        switchMap(() => this.api.list().pipe(catchError((e) => (this.error.set(problemMessage(e)), EMPTY)))),
        takeUntilDestroyed(),
      )
      .subscribe((claims) => this.claims.set(claims));
    this.refresh
      .pipe(switchMap(() => this.api.list()), takeUntilDestroyed())
      .subscribe((claims) => this.claims.set(claims));
  }

  protected pickUp(claim: ClaimSummary): void {
    this.busyId.set(claim.id);
    this.error.set(null);
    this.api.assign(claim.id).subscribe({
      next: () => this.router.navigate(['/work', claim.id]),
      error: (e) => {
        this.busyId.set(null);
        this.error.set(
          e instanceof HttpErrorResponse && e.status === 409
            ? `Someone else picked up ${claim.number} first.`
            : problemMessage(e),
        );
        this.refresh.next();
      },
    });
  }
}
