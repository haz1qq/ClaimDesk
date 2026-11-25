import { Component, computed, input } from '@angular/core';
import { ClaimStatus } from '../core/models';

const LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  AwaitingInformation: 'Awaiting information',
  Settled: 'Settled',
  Rejected: 'Rejected',
};

/** Status chip. The text carries the meaning; colour only reinforces it. */
@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="'badge badge-' + status()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();
  protected readonly label = computed(() => LABELS[this.status()]);
}
