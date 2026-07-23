import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { PublicGame } from 'shared';
import { NotAvailable } from '../../ui/not-available/not-available';

export interface GameDetailsDialogData {
  game: PublicGame;
  isSelected: boolean;
}

@Component({
  selector: 'app-game-details-dialog',
  imports: [MatDialogModule, MatButtonModule, NotAvailable],
  templateUrl: './game-details-dialog.html',
  styleUrl: './game-details-dialog.scss',
})
export class GameDetailsDialog {
  protected readonly data = inject<GameDetailsDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<GameDetailsDialog>);

  protected select(): void {
    this.dialogRef.close('select');
  }
}
