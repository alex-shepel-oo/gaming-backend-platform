import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type StatusPillVariant = 'success' | 'warning' | 'error' | 'progress' | 'neutral';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'lib-status-pill',
  templateUrl: './status-pill.html',
  styleUrl: './status-pill.scss',
})
export class StatusPill {
  readonly variant = input.required<StatusPillVariant>();
  readonly label = input.required<string>();
}
