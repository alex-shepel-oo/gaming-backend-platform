import { signal } from '@angular/core';
import { Observable } from 'rxjs';

// The loading/error-signal-pair-plus-subscribe shape repeated across nearly
// every admin-client screen's "fetch this, show a spinner, flag an error
// banner on failure" load method. Centralizes that bookkeeping only -- each
// call site still owns its own success handling via onSuccess, and still
// decides what (if anything) else an error should also affect.
export class Loadable {
  readonly loading = signal(true);
  readonly error = signal(false);

  load<T>(source: Observable<T>, onSuccess: (value: T) => void): void {
    this.loading.set(true);
    this.error.set(false);

    source.subscribe({
      next: (value) => {
        onSuccess(value);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }
}
