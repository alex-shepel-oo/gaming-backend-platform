import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DatePipe } from '@angular/common';
import { ProfileService } from 'shared';
import { Avatar } from '../ui/avatar/avatar';
import { NotAvailable } from '../ui/not-available/not-available';

@Component({
  selector: 'app-profile',
  imports: [
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    ReactiveFormsModule,
    DatePipe,
    Avatar,
    NotAvailable,
  ],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile {
  private readonly formBuilder = inject(FormBuilder);

  protected readonly profileService = inject(ProfileService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly editForm = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(64)]],
    avatarUrl: [''],
  });

  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);
  protected readonly saved = signal(false);

  constructor() {
    this.profileService.refreshProfile().subscribe({
      next: (profile) => {
        this.loading.set(false);
        this.editForm.setValue({
          displayName: profile.displayName,
          avatarUrl: profile.avatarUrl ?? '',
        });
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
}
