import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WipOverlay } from './wip-overlay';

@Component({
  imports: [WipOverlay],
  template: `<lib-wip-overlay><p>Hidden content</p></lib-wip-overlay>`,
})
class HostComponent {}

describe('WipOverlay', () => {
  it('shows the default label and projects the wrapped content', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Work in progress');
    expect(host.textContent).toContain('Hidden content');
  });

  it('shows a custom label when provided', () => {
    const fixture = TestBed.createComponent(WipOverlay);
    fixture.componentRef.setInput('label', 'Coming soon');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Coming soon');
  });
});
