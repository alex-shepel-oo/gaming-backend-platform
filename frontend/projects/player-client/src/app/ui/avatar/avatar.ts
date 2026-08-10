import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { colorFor, initialsOf } from 'shared';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-avatar',
  templateUrl: './avatar.html',
  styleUrl: './avatar.scss',
})
export class Avatar {
  readonly name = input.required<string>();
  readonly avatarUrl = input<string | null>(null);

  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly color = computed(() => colorFor(this.name()));

  // Falls back to the initials placeholder when the URL 404s/CORS-fails/etc,
  // same as the currency/game icon fallbacks elsewhere in this app. Tracks
  // the specific URL that failed (not just a bare flag) so a *new*
  // avatarUrl (e.g. after editing the profile) gets its own fresh attempt
  // instead of staying stuck on the old failure.
  private readonly failedUrl = signal<string | null>(null);
  protected readonly showImage = computed(
    () => this.avatarUrl() !== null && this.avatarUrl() !== this.failedUrl(),
  );

  protected onImageError(): void {
    this.failedUrl.set(this.avatarUrl());
  }
}
