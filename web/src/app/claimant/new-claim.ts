import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ClaimsApi, problemMessage } from '../core/claims-api.service';
import { ClaimType } from '../core/models';

@Component({
  selector: 'app-new-claim',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page-header">
      <h1>Report an incident</h1>
    </div>

    <form class="card form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div class="field">
        <label for="type">What happened to?</label>
        <select id="type" formControlName="type">
          <option value="Motor">My vehicle (motor)</option>
          <option value="Property">My home or property</option>
        </select>
      </div>

      <div class="field">
        <label for="incidentDate">Date of the incident</label>
        <input id="incidentDate" type="date" formControlName="incidentDate" [max]="today" />
        @if (showError('incidentDate')) {
          <p class="field-error">Enter the date of the incident.</p>
        }
      </div>

      <div class="field">
        <label for="description">What happened?</label>
        <textarea id="description" rows="4" formControlName="description"
          placeholder="Where and how it happened, and what was damaged."></textarea>
        @if (showError('description')) {
          <p class="field-error">Describe what happened.</p>
        }
      </div>

      <div class="field">
        <label for="claimedAmount">Estimated cost</label>
        <input id="claimedAmount" type="number" min="0.01" step="0.01" inputmode="decimal" formControlName="claimedAmount" />
        <p class="hint">Your best estimate, in your local currency. We'll confirm the amount during review.</p>
        @if (showError('claimedAmount')) {
          <p class="field-error">Enter an amount greater than zero.</p>
        }
      </div>

      @if (error()) {
        <p class="alert alert-error" role="alert">{{ error() }}</p>
      }

      <div class="actions">
        <button class="btn" type="submit" [disabled]="submitting()">
          {{ submitting() ? 'Submitting…' : 'Submit claim' }}
        </button>
        <a class="btn btn-secondary" routerLink="/claims">Cancel</a>
      </div>
    </form>
  `,
})
export class NewClaim {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);

  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    type: ['Motor' as ClaimType, Validators.required],
    incidentDate: ['', Validators.required],
    description: ['', Validators.required],
    claimedAmount: [0, [Validators.required, Validators.min(0.01)]],
  });

  protected showError(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    // Server rules (e.g. no future dates) are the source of truth; its message is shown as-is.
    this.api.submit(this.form.getRawValue()).subscribe({
      next: (claim) => this.router.navigate(['/claims', claim.summary.id]),
      error: (e) => {
        this.error.set(problemMessage(e));
        this.submitting.set(false);
      },
    });
  }
}
