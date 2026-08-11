import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Balance, NotAvailable, PublicGame } from 'shared';

export interface GameDetailsDialogData {
  game: PublicGame;
  balances: Balance[];
}

// Read-only, shown only on mobile, where the card itself doesn't have room
// for the balance/description info a desktop card shows inline.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-details-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, NotAvailable],
  templateUrl: './game-details-dialog.html',
  styleUrl: './game-details-dialog.scss',
})
export class GameDetailsDialog {
  protected readonly data = inject<GameDetailsDialogData>(MAT_DIALOG_DATA);

  // Single game per dialog instance, no per-item key needed like the
  // list-rendered icons in Wallet/Shell/Games.
  protected readonly iconFailed = signal(false);
}
