import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  forwardRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';

export interface SelectDropdownOption {
  readonly id: string;
  readonly label: string;
}

/**
 * A plain, JS-positioned dropdown -- not a native <select>. Two rounds of
 * CSS Customizable Select (`appearance: base-select`, `::picker(select)`),
 * first with `anchor-size(width)` then with a ResizeObserver-fed custom
 * property, both failed to reliably size the popup to the trigger's width in
 * real browsers (confirmed live, twice, not just in this environment's own
 * preview pane). Measuring the trigger's actual box at the moment it opens,
 * with a plain getBoundingClientRect(), sidesteps all of that: no anchor
 * positioning, no custom-property inheritance into a top-layer pseudo-
 * element, nothing dependent on cutting-edge/inconsistent browser support.
 */
@Component({
  selector: 'app-select-dropdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule],
  templateUrl: './select-dropdown.html',
  styleUrl: './select-dropdown.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => SelectDropdown),
      multi: true,
    },
  ],
  host: {
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class SelectDropdown implements ControlValueAccessor {
  readonly options = input.required<SelectDropdownOption[]>();
  // A ticker with exactly one reachable option (see convert.ts's
  // toCurrencyIsChoice()) isn't a real choice for the player -- this renders
  // it as plain, non-interactive text instead of a control that opens onto a
  // single, already-selected row.
  readonly readonlyMode = input(false);

  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

  protected readonly isOpen = signal(false);
  protected readonly disabled = signal(false);
  protected readonly popupWidthPx = signal(0);
  protected readonly activeIndex = signal(-1);

  private readonly value = signal('');

  protected readonly selectedLabel = computed(
    () => this.options().find((option) => option.id === this.value())?.label ?? '',
  );

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected isSelected(option: SelectDropdownOption): boolean {
    return option.id === this.value();
  }

  protected toggle(): void {
    if (this.disabled() || this.readonlyMode()) {
      return;
    }

    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  protected selectOption(option: SelectDropdownOption): void {
    this.value.set(option.id);
    this.onChange(option.id);
    this.close();
    this.trigger()?.nativeElement.focus();
  }

  protected onTriggerKeydown(event: KeyboardEvent): void {
    if (this.disabled() || this.readonlyMode()) {
      return;
    }

    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();

        if (!this.isOpen()) {
          this.open();
        } else {
          this.moveActive(event.key === 'ArrowDown' ? 1 : -1);
        }

        break;
      case 'Enter':
      case ' ':
        event.preventDefault();

        if (this.isOpen() && this.activeIndex() >= 0) {
          this.selectOption(this.options()[this.activeIndex()]);
        } else {
          this.toggle();
        }

        break;
      case 'Escape':
        if (this.isOpen()) {
          event.preventDefault();
          this.close();
        }

        break;
    }
  }

  private open(): void {
    const triggerEl = this.trigger()?.nativeElement;

    if (!triggerEl) {
      return;
    }

    // Reads the trigger's own box, not a parent's -- reaching into
    // elementRef.nativeElement.parentElement would only coincidentally give
    // the right number here, by assuming a specific wrapping div this
    // component doesn't actually control. Now that :host is a real flex box
    // (see select-dropdown.scss) instead of display:contents, the trigger's
    // own width IS the width its container gave it.
    this.popupWidthPx.set(triggerEl.getBoundingClientRect().width);
    this.activeIndex.set(Math.max(0, this.options().findIndex((option) => option.id === this.value())));
    this.isOpen.set(true);
  }

  private close(): void {
    if (!this.isOpen()) {
      return;
    }

    this.isOpen.set(false);
    this.onTouched();
  }

  private moveActive(delta: number): void {
    const count = this.options().length;

    if (count === 0) {
      return;
    }

    this.activeIndex.set((this.activeIndex() + delta + count) % count);
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }
}
