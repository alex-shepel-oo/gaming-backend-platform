import { Component, computed, input } from '@angular/core';

export function initialsOf(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('');
}

// Deterministic, derived only from the real display name -- not fabricated
// data, just a stable color so the same player always gets the same avatar.
export function colorFor(name: string): string {
  let hash = 0;

  for (const char of name) {
    hash = (hash * 31 + char.charCodeAt(0)) | 0;
  }

  return `hsl(${Math.abs(hash) % 360}, 55%, 40%)`;
}

@Component({
  selector: 'app-avatar',
  templateUrl: './avatar.html',
  styleUrl: './avatar.scss',
})
export class Avatar {
  readonly name = input.required<string>();

  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly color = computed(() => colorFor(this.name()));
}
