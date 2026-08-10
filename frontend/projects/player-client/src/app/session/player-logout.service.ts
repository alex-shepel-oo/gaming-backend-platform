import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, GameSelectionService, NotificationHubService, ProfileService, WalletService } from 'shared';

// Shared by the shell toolbar and the profile screen -- both need the exact
// same teardown sequence (server-side session, then every client-side cache
// a new session shouldn't inherit) before landing back on the login screen.
@Injectable({ providedIn: 'root' })
export class PlayerLogoutService {
  private readonly authService = inject(AuthService);
  private readonly gameSelection = inject(GameSelectionService);
  private readonly walletService = inject(WalletService);
  private readonly profileService = inject(ProfileService);
  private readonly notificationHub = inject(NotificationHubService);
  private readonly router = inject(Router);

  logout(): void {
    this.authService.logout().subscribe(() => {
      this.gameSelection.clear();
      this.walletService.clearBalances();
      this.profileService.clearProfile();
      this.notificationHub.disconnect();
      this.router.navigateByUrl('/login');
    });
  }
}
