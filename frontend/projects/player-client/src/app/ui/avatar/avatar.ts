import { Component, computed, input } from '@angular/core';
import { colorFor, initialsOf } from 'shared';

@Component({
  selector: 'app-avatar',
  templateUrl: './avatar.html',
  styleUrl: './avatar.scss',
})
export class Avatar {
  readonly name = input.required<string>();
  readonly avatarUrl = input<string | null>(null);

  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly color = computed(() => colorFor(this.name()));
}
