import { TestBed } from '@angular/core/testing';
import { TokenStore } from 'shared';
import { AdminDashboard } from './admin-dashboard';

function fakeAccessToken(scope: string): string {
  const payload = { sub: 'user-1', email: 'admin@example.com', name: 'Admin One', scope };

  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

describe('AdminDashboard', () => {
  it('shows who is signed in', () => {
    TestBed.configureTestingModule({ imports: [AdminDashboard] });

    const tokenStore = TestBed.inject(TokenStore);
    tokenStore.set(fakeAccessToken('platform'));

    const fixture = TestBed.createComponent(AdminDashboard);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('admin@example.com');
    expect(text).toContain('platform');
  });
});
