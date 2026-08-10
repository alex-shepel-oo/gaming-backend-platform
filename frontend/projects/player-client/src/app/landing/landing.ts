import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-landing',
  imports: [MatButtonModule, MatIconModule, RouterLink],
  templateUrl: './landing.html',
  styleUrl: './landing.scss',
})
export class Landing {}
