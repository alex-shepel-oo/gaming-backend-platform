import { Subject } from 'rxjs';
import { Loadable } from './loadable';

describe('Loadable', () => {
  it('starts loading, then flips to loaded with the value handed to onSuccess', () => {
    const loadable = new Loadable();
    const source = new Subject<string>();
    let received: string | undefined;

    loadable.load(source, (value) => (received = value));

    expect(loadable.loading()).toBe(true);
    expect(loadable.error()).toBe(false);

    source.next('hello');
    source.complete();

    expect(loadable.loading()).toBe(false);
    expect(loadable.error()).toBe(false);
    expect(received).toBe('hello');
  });

  it('flips to error, not loading, on failure', () => {
    const loadable = new Loadable();
    const source = new Subject<string>();

    loadable.load(source, () => {});
    source.error(new Error('boom'));

    expect(loadable.loading()).toBe(false);
    expect(loadable.error()).toBe(true);
  });

  it('resets loading/error at the start of a new load call, even after a prior failure', () => {
    const loadable = new Loadable();

    const first = new Subject<string>();
    loadable.load(first, () => {});
    first.error(new Error('boom'));
    expect(loadable.error()).toBe(true);

    const second = new Subject<string>();
    loadable.load(second, () => {});
    expect(loadable.loading()).toBe(true);
    expect(loadable.error()).toBe(false);
  });
});
