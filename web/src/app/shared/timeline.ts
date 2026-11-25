import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { ClaimEventType, TimelineEntry } from '../core/models';

const LABELS: Record<ClaimEventType, string> = {
  ClaimSubmitted: 'Claim submitted',
  ClaimAssigned: 'Picked up for review',
  InformationRequested: 'More information requested',
  InformationProvided: 'Information provided',
  ReserveAdjusted: 'Reserve adjusted',
  ClaimSettled: 'Claim settled',
  ClaimRejected: 'Claim rejected',
};

/** The claim's history, oldest first. The API already leaves internal events out for claimants. */
@Component({
  selector: 'app-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline">
      @for (entry of entries(); track $index) {
        <li>
          <span class="timeline-label">{{ label(entry.type) }}</span>
          <span class="muted">{{ entry.actorName }} · {{ entry.occurredAt | date: 'd MMM y, HH:mm' }}</span>
        </li>
      }
    </ol>
  `,
})
export class Timeline {
  readonly entries = input.required<TimelineEntry[]>();

  protected label(type: ClaimEventType): string {
    return LABELS[type];
  }
}
