import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';

/**
 * Wraps a delegate SpanProcessor to tag every span it sees with the standard OTel `enduser.id`
 * attribute -- the same name and intent as the backend's EnduserIdEnricher/EnduserIdMiddleware
 * (see .plan/services.md's "Per-user correlation" note). `setUserId` is called once, after login,
 * not resolved per span, so every span from then on picks up the current user until it changes or
 * clears.
 */
export class EnduserIdSpanProcessor implements SpanProcessor {
  private userId: string | null = null;

  constructor(private readonly delegate: SpanProcessor) {}

  setUserId(userId: string | null): void {
    this.userId = userId;
  }

  onStart(span: Span, parentContext: Context): void {
    if (this.userId) {
      span.setAttribute('enduser.id', this.userId);
    }

    this.delegate.onStart(span, parentContext);
  }

  onEnd(span: ReadableSpan): void {
    this.delegate.onEnd(span);
  }

  forceFlush(): Promise<void> {
    return this.delegate.forceFlush();
  }

  shutdown(): Promise<void> {
    return this.delegate.shutdown();
  }
}
