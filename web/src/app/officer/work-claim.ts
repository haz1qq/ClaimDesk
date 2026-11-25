import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, interval, Observable, startWith, switchMap } from 'rxjs';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { CurrentUserService } from '../core/current-user.service';
import { ClaimDetail } from '../core/models';
import { StatusBadge } from '../shared/status-badge';
import { Timeline } from '../shared/timeline';

/**
 * The officer's workspace for one claim. Buttons only appear in the states where the server allows them;
 * the server still enforces every rule, and its message is shown if a rule is broken.
 */
@Component({
  selector: 'app-work-claim',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule, RouterLink, StatusBadge, Timeline],
  template: `
    <p><a routerLink="/work">← Work queue</a></p>

    @if (error()) {
      <p class="alert alert-error" role="alert">{{ error() }}</p>
    }

    @if (claim(); as c) {
      <div class="page-header">
        <h1>{{ c.summary.number }} · {{ c.summary.claimantName }}</h1>
        <app-status-badge [status]="c.summary.status" />
      </div>

      @if (c.summary.status === 'Submitted') {
        <p class="card">
          Nobody has picked this claim up yet.
          <button class="btn" type="button" (click)="pickUp()" [disabled]="busy()">Pick up</button>
        </p>
      } @else if (!isMine() && c.summary.assignedOfficerName) {
        <p class="card muted">Assigned to {{ c.summary.assignedOfficerName }}. Only they can act on it.</p>
      }

      <div class="grid-2">
        <section class="card">
          <h2>Claim</h2>
          <dl class="details">
            <dt>Type</dt><dd>{{ c.summary.type }} · {{ c.summary.market }}</dd>
            <dt>Incident date</dt><dd>{{ c.incidentDate | date: 'd MMM y' }}</dd>
            <dt>Claimed</dt><dd>{{ c.summary.currency }} {{ c.summary.claimedAmount | number: '1.2-2' }}</dd>
            <dt>Reserve</dt><dd><strong>{{ c.summary.currency }} {{ c.summary.reserve | number: '1.2-2' }}</strong></dd>
            @if (c.settlementAmount !== null) {
              <dt>Settled for</dt><dd>{{ c.summary.currency }} {{ c.settlementAmount | number: '1.2-2' }}</dd>
            }
            @if (c.rejectionReason) {
              <dt>Rejected</dt><dd>{{ c.rejectionReason }}</dd>
            }
            <dt>What happened</dt><dd class="prewrap">{{ c.description }}</dd>
          </dl>

          @if (c.informationRequests.length > 0) {
            <h2>Questions to the claimant</h2>
            @for (request of c.informationRequests; track request.id) {
              <p><strong>Q:</strong> {{ request.question }}<br />
                <strong>A:</strong> {{ request.response ?? 'Waiting for the claimant…' }}</p>
            }
          }
        </section>

        <section class="card">
          <h2>Timeline</h2>
          <app-timeline [entries]="c.timeline" />
        </section>
      </div>

      @if (isMine() && c.summary.status !== 'Settled' && c.summary.status !== 'Rejected') {
        <section class="card stack" aria-labelledby="actions-heading" style="margin-top: var(--space-4)">
          <h2 id="actions-heading">Actions</h2>

          @if (canDecide()) {
            <div class="inline-form">
              <div class="field">
                <label for="question">Ask the claimant for information</label>
                <input id="question" [formControl]="question" placeholder="e.g. Please share the police report number." />
              </div>
              <button class="btn btn-secondary" type="button" (click)="requestInformation()" [disabled]="question.invalid || busy()">Send question</button>
            </div>
          } @else {
            <p class="muted">Waiting for the claimant to answer. You can still adjust the reserve.</p>
          }

          <div class="inline-form">
            <div class="field">
              <label for="reserve">Reserve ({{ c.summary.currency }})</label>
              <input id="reserve" type="number" min="0" step="0.01" [formControl]="reserve" />
            </div>
            <button class="btn btn-secondary" type="button" (click)="adjustReserve()" [disabled]="reserve.invalid || busy()">Update reserve</button>
          </div>

          @if (canDecide()) {
            <div class="inline-form">
              <div class="field">
                <label for="amount">Settlement amount ({{ c.summary.currency }})</label>
                <input id="amount" type="number" min="0.01" step="0.01" [formControl]="amount" />
              </div>
              <button class="btn" type="button" (click)="settle()" [disabled]="amount.invalid || busy()">Settle claim</button>
            </div>

            <div class="inline-form">
              <div class="field">
                <label for="reason">Rejection reason</label>
                <input id="reason" [formControl]="reason" placeholder="e.g. Not covered by the policy." />
              </div>
              <button class="btn btn-danger" type="button" (click)="reject()" [disabled]="reason.invalid || busy()">Reject claim</button>
            </div>
          }
        </section>
      }
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class WorkClaim {
  private readonly api = inject(ClaimsApi);
  private readonly currentUser = inject(CurrentUserService);

  readonly id = input.required<string>(); // bound from the :id route parameter
  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly question = new FormControl('', { nonNullable: true, validators: Validators.required });
  protected readonly reserve = new FormControl<number | null>(null, [Validators.required, Validators.min(0)]);
  protected readonly amount = new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: Validators.required });

  // These mirror the server's rules so we only offer actions that can succeed.
  protected readonly isMine = computed(() => this.claim()?.summary.assignedOfficerId === this.currentUser.userId());
  protected readonly canDecide = computed(() => this.isMine() && this.claim()?.summary.status === 'UnderReview');

  constructor() {
    toObservable(this.id)
      .pipe(
        switchMap((id) => interval(10_000).pipe(startWith(0), switchMap(() => this.api.get(id)))),
        catchError((e) => (this.error.set(problemMessage(e)), EMPTY)),
        takeUntilDestroyed(),
      )
      .subscribe((claim) => this.claim.set(claim));
  }

  protected pickUp(): void {
    this.act(this.api.assign(this.id()));
  }

  protected requestInformation(): void {
    this.act(this.api.requestInformation(this.id(), this.question.value), this.question);
  }

  protected adjustReserve(): void {
    this.act(this.api.adjustReserve(this.id(), this.reserve.value!), this.reserve);
  }

  protected settle(): void {
    this.act(this.api.settle(this.id(), this.amount.value!), this.amount);
  }

  protected reject(): void {
    this.act(this.api.reject(this.id(), this.reason.value), this.reason);
  }

  /** Every action: call the API, show the updated claim, clear the form that was used. */
  private act(call: Observable<ClaimDetail>, usedControl?: AbstractControl): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (claim) => {
        this.claim.set(claim);
        usedControl?.reset();
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.busy.set(false);
      },
    });
  }
}
