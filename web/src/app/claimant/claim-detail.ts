import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, interval, startWith, switchMap } from 'rxjs';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { ClaimDetail } from '../core/models';
import { StatusBadge } from '../shared/status-badge';
import { Timeline } from '../shared/timeline';

/** The claimant's view of one claim: progress, decision, and any question waiting for them. */
@Component({
  selector: 'app-claim-detail',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule, RouterLink, StatusBadge, Timeline],
  template: `
    <p><a routerLink="/claims">← My claims</a></p>

    @if (error()) {
      <p class="alert alert-error" role="alert">{{ error() }}</p>
    }

    @if (claim(); as c) {
      <div class="page-header">
        <h1>{{ c.summary.number }}</h1>
        <app-status-badge [status]="c.summary.status" />
      </div>

      @if (openRequest(); as request) {
        <section class="card callout" aria-labelledby="question-heading">
          <h2 id="question-heading">We need more information</h2>
          <p class="question">{{ request.question }}</p>
          <div class="field">
            <label for="response">Your answer</label>
            <textarea id="response" rows="3" [formControl]="response"></textarea>
          </div>
          <button class="btn" type="button" (click)="respond(request.id)" [disabled]="response.invalid || busy()">
            {{ busy() ? 'Sending…' : 'Send answer' }}
          </button>
        </section>
      }

      @if (c.summary.status === 'Settled') {
        <p class="alert alert-success">
          Settled for {{ c.summary.currency }} {{ c.settlementAmount | number: '1.2-2' }} on {{ c.decidedAt | date: 'd MMM y' }}.
        </p>
      }
      @if (c.summary.status === 'Rejected') {
        <p class="alert alert-error">Not approved: {{ c.rejectionReason }}</p>
      }

      <div class="grid-2">
        <section class="card">
          <h2>Details</h2>
          <dl class="details">
            <dt>Type</dt><dd>{{ c.summary.type }}</dd>
            <dt>Incident date</dt><dd>{{ c.incidentDate | date: 'd MMM y' }}</dd>
            <dt>Estimated cost</dt><dd>{{ c.summary.currency }} {{ c.summary.claimedAmount | number: '1.2-2' }}</dd>
            <dt>Handled by</dt><dd>{{ c.summary.assignedOfficerName ?? 'Waiting to be picked up' }}</dd>
            <dt>What happened</dt><dd class="prewrap">{{ c.description }}</dd>
          </dl>
        </section>
        <section class="card">
          <h2>Progress</h2>
          <app-timeline [entries]="c.timeline" />
        </section>
      </div>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class ClaimDetailPage {
  private readonly api = inject(ClaimsApi);

  readonly id = input.required<string>(); // bound from the :id route parameter
  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly response = new FormControl('', { nonNullable: true, validators: Validators.required });

  protected readonly openRequest = computed(() =>
    this.claim()?.summary.status === 'AwaitingInformation'
      ? (this.claim()!.informationRequests.find((r) => r.response === null) ?? null)
      : null,
  );

  constructor() {
    // Load now, then refresh every 10 seconds so a decision shows up without reloading.
    toObservable(this.id)
      .pipe(
        switchMap((id) => interval(10_000).pipe(startWith(0), switchMap(() => this.api.get(id)))),
        catchError((e) => (this.error.set(problemMessage(e)), EMPTY)),
        takeUntilDestroyed(),
      )
      .subscribe((claim) => this.claim.set(claim));
  }

  protected respond(requestId: string): void {
    this.busy.set(true);
    this.api.respond(this.id(), requestId, this.response.value).subscribe({
      next: (claim) => {
        this.claim.set(claim);
        this.response.reset();
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.busy.set(false);
      },
    });
  }
}
