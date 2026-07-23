import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-not-available',
  imports: [MatIconModule],
  templateUrl: './not-available.html',
  styleUrl: './not-available.scss',
})
export class NotAvailable {
  readonly message = input('Not available yet');
}
