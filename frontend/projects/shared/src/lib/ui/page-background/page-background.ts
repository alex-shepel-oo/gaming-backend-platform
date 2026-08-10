import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'lib-page-background',
  templateUrl: './page-background.html',
  styleUrl: './page-background.scss',
})
export class PageBackground {}
