import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, interval, startWith, switchMap } from 'rxjs';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { Dashboard } from '../core/models';
import { StatusBadge } from '../shared/status-badge';

/** Team workload and liability exposure, refreshed every 10 seconds (officers and managers). */
@Component({
  selector: 'app-dashboard',
  imports: [DecimalPipe, StatusBadge],
  template: `
    <div class="page-header">
      <h1>Dashboard</h1>
      <span class="muted">Updates every 10 seconds</span>
    </div>

    @if (error()) {
      <p class="alert alert-error" role="alert">{{ error() }}</p>
    }

    @if (data(); as d) {
      <div class="stats">
        <div class="card stat"><span class="stat-value">{{ totalOpen() }}</span><span class="muted">Open claims</span></div>
        <div class="card stat"><span class="stat-value">{{ d.unassigned }}</span><span class="muted">Waiting to be picked up</span></div>
        <div class="card stat"><span class="stat-value">{{ d.openOver7Days }}</span><span class="muted">Open more than 7 days</span></div>
        <div class="card stat"><span class="stat-value">{{ d.openOver30Days }}</span><span class="muted">Open more than 30 days</span></div>
      </div>

      <div class="grid-2">
        <section class="card" aria-labelledby="exposure-heading">
          <h2 id="exposure-heading">Liability exposure</h2>
          <p class="hint">Sum of reserves on open claims, per currency. Currencies are not converted or added together.</p>
          <table>
            <thead><tr><th>Currency</th><th class="num">Open claims</th><th class="num">Total reserve</th></tr></thead>
            <tbody>
              @for (row of d.exposure; track row.currency) {
                <tr><td>{{ row.currency }}</td><td class="num">{{ row.openClaims }}</td><td class="num">{{ row.totalReserve | number: '1.2-2' }}</td></tr>
              } @empty {
                <tr><td colspan="3" class="muted">No open claims.</td></tr>
              }
            </tbody>
          </table>
        </section>

        <section class="card" aria-labelledby="status-heading">
          <h2 id="status-heading">Open claims by status</h2>
          <table>
            <thead><tr><th>Status</th><th class="num">Claims</th></tr></thead>
            <tbody>
              @for (row of d.openByStatus; track row.status) {
                <tr><td><app-status-badge [status]="row.status" /></td><td class="num">{{ row.count }}</td></tr>
              } @empty {
                <tr><td colspan="2" class="muted">No open claims.</td></tr>
              }
            </tbody>
          </table>
        </section>
      </div>

      <section class="card table-wrap" aria-labelledby="team-heading" style="margin-top: var(--space-4)">
        <h2 id="team-heading" style="padding: var(--space-4) var(--space-4) 0">Team workload</h2>
        <table>
          <thead>
            <tr><th>Officer</th><th class="num">Open</th><th class="num">Closed (30 days)</th><th class="num">Avg days to decision</th></tr>
          </thead>
          <tbody>
            @for (officer of d.officers; track officer.officerId) {
              <tr>
                <td>{{ officer.name }}</td>
                <td class="num">{{ officer.open }}</td>
                <td class="num">{{ officer.closedLast30Days }}</td>
                <td class="num">{{ officer.avgDaysToDecision === null ? '–' : (officer.avgDaysToDecision | number: '1.1-1') }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(ClaimsApi);
  protected readonly data = signal<Dashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly totalOpen = computed(() => this.data()?.openByStatus.reduce((sum, s) => sum + s.count, 0) ?? 0);

  constructor() {
    interval(10_000)
      .pipe(
        startWith(0),
        switchMap(() => this.api.dashboard().pipe(catchError((e) => (this.error.set(problemMessage(e)), EMPTY)))),
        takeUntilDestroyed(),
      )
      .subscribe((data) => {
        this.error.set(null);
        this.data.set(data);
      });
  }
}
