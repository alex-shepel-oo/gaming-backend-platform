import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs/operators';
import { AuthService, TokenStore } from 'shared';

// Matches the site's own mobile breakpoint (admin-shell.scss's drawer switch).
const MOBILE_BREAKPOINT = '(max-width: 767px)';

// A plain CSS class-toggle drawer, not mat-sidenav: this one's `mode` needs
// to change between 'side' and 'over' as the viewport crosses the breakpoint,
// simpler to own directly than route a runtime mode change through MatDrawer.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'admin-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule],
  templateUrl: './admin-shell.html',
  styleUrl: './admin-shell.scss',
})
export class AdminShell {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly breakpointObserver = inject(BreakpointObserver);

  protected readonly tokenStore = inject(TokenStore);

  // initialValue reads the query synchronously (not a hardcoded false) so
  // the very first paint already reflects the real viewport.
  protected readonly isMobile = toSignal(
    this.breakpointObserver.observe(MOBILE_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: this.breakpointObserver.isMatched(MOBILE_BREAKPOINT) },
  );
  protected readonly mobileMenuOpen = signal(false);

  protected toggleMobileMenu(): void {
    this.mobileMenuOpen.update((open) => !open);
  }

  protected closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  protected logout(): void {
    this.authService.logout().subscribe(() => this.router.navigateByUrl('/login'));
  }
}
