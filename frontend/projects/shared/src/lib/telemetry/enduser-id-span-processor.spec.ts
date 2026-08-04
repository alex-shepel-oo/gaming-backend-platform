import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';
import { EnduserIdSpanProcessor } from './enduser-id-span-processor';

function createFakeDelegate(): SpanProcessor {
  return {
    onStart: vi.fn(),
    onEnd: vi.fn(),
    forceFlush: vi.fn().mockResolvedValue(undefined),
    shutdown: vi.fn().mockResolvedValue(undefined),
  };
}

describe('EnduserIdSpanProcessor', () => {
  it('does not tag a span when no user id has been set yet', () => {
    const delegate = createFakeDelegate();
    const processor = new EnduserIdSpanProcessor(delegate);
    const span = { setAttribute: vi.fn() } as unknown as Span;

    processor.onStart(span, {} as Context);

    expect(span.setAttribute).not.toHaveBeenCalled();
    expect(delegate.onStart).toHaveBeenCalledWith(span, {});
  });

  it('tags every span with enduser.id once a user id has been set', () => {
    const delegate = createFakeDelegate();
    const processor = new EnduserIdSpanProcessor(delegate);
    processor.setUserId('user-42');

    const firstSpan = { setAttribute: vi.fn() } as unknown as Span;
    const secondSpan = { setAttribute: vi.fn() } as unknown as Span;

    processor.onStart(firstSpan, {} as Context);
    processor.onStart(secondSpan, {} as Context);

    expect(firstSpan.setAttribute).toHaveBeenCalledWith('enduser.id', 'user-42');
    expect(secondSpan.setAttribute).toHaveBeenCalledWith('enduser.id', 'user-42');
  });

  it('stops tagging once the user id is cleared, e.g. on logout', () => {
    const delegate = createFakeDelegate();
    const processor = new EnduserIdSpanProcessor(delegate);
    processor.setUserId('user-42');
    processor.setUserId(null);

    const span = { setAttribute: vi.fn() } as unknown as Span;
    processor.onStart(span, {} as Context);

    expect(span.setAttribute).not.toHaveBeenCalled();
  });

  it('delegates onEnd, forceFlush and shutdown unchanged', async () => {
    const delegate = createFakeDelegate();
    const processor = new EnduserIdSpanProcessor(delegate);
    const span = {} as ReadableSpan;

    processor.onEnd(span);
    await processor.forceFlush();
    await processor.shutdown();

    expect(delegate.onEnd).toHaveBeenCalledWith(span);
    expect(delegate.forceFlush).toHaveBeenCalled();
    expect(delegate.shutdown).toHaveBeenCalled();
  });
});
