import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DatePipe } from '@angular/common';
import { CurrencyScope, NotAvailable, PageBackground, ProfileService, WalletService } from 'shared';
import { Avatar } from '../ui/avatar/avatar';
import { PlayerLogoutService } from '../session/player-logout.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-profile',
  imports: [MatIconModule, MatProgressSpinnerModule, ReactiveFormsModule, DatePipe, Avatar, NotAvailable, PageBackground],
  templateUrl: './profile.html',
  styleUrls: ['./profile.scss', './profile-edit.scss'],
})
export class Profile {
  private readonly formBuilder = inject(FormBuilder);
  private readonly playerLogout = inject(PlayerLogoutService);
  private readonly walletService = inject(WalletService);

  protected readonly profileService = inject(ProfileService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly editForm = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(64)]],
  });

  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);
  protected readonly saved = signal(false);

  // Mirrors the mockup's "Wallet Balance" card: the platform currency the
  // player actually holds, not a fictional single "CODE" total.
  protected readonly platformBalances = computed(
    () => this.walletService.balances()?.filter((balance) => balance.scope === CurrencyScope.Platform) ?? [],
  );

  constructor() {
    this.walletService.refreshBalances().subscribe();

    this.profileService.refreshProfile().subscribe({
      next: (profile) => {
        this.loading.set(false);
        this.editForm.setValue({ displayName: profile.displayName });
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  protected submitEdit(): void {
    if (this.editForm.invalid) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(false);
    this.saved.set(false);

    this.profileService.updateMe(this.editForm.getRawValue()).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set(true);
      },
    });
  }

  protected cancelEdit(): void {
    const profile = this.profileService.profile();

    if (!profile) {
      return;
    }

    this.editForm.setValue({ displayName: profile.displayName });
    this.saveError.set(false);
    this.saved.set(false);
  }

  protected logout(): void {
    this.playerLogout.logout();
  }
}
