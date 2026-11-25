import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
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

/** The claim's history, oldest first. Claimants don't see internal reserve changes. */
@Component({
  selector: 'app-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline">
      @for (entry of visible(); track $index) {
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
  readonly internal = input(false);

  protected readonly visible = computed(() =>
    this.internal() ? this.entries() : this.entries().filter((e) => e.type !== 'ReserveAdjusted'),
  );

  protected label(type: ClaimEventType): string {
    return LABELS[type];
  }
}
