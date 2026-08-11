import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'lib-wip-overlay',
  imports: [MatIconModule],
  templateUrl: './wip-overlay.html',
  styleUrl: './wip-overlay.scss',
})
export class WipOverlay {
  readonly label = input('Work in progress');
}
