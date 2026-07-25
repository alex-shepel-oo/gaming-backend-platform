import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  AuthService,
  CurrencyScope,
  GameSelectionService,
  NotificationHubService,
  TokenStore,
  WalletService,
} from 'shared';
import { Avatar } from '../ui/avatar/avatar';
import { ThemeService } from '../theme/theme.service';

@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    Avatar,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly authService = inject(AuthService);
  private readonly walletService = inject(WalletService);
  private readonly notificationHub = inject(NotificationHubService);
  private readonly router = inject(Router);

  protected readonly gameSelection = inject(GameSelectionService);
  protected readonly tokenStore = inject(TokenStore);
  protected readonly theme = inject(ThemeService);

  protected readonly platformBalances = computed(
    () => this.walletService.balances()?.filter((balance) => balance.scope === CurrencyScope.Platform) ?? [],
  );

  constructor() {
    this.walletService.refreshBalances().subscribe();
    this.notificationHub.connect();
  }

  protected logout(): void {
    this.authService.logout().subscribe(() => {
      this.gameSelection.clear();
      this.walletService.clearBalances();
      this.notificationHub.disconnect();
      this.router.navigateByUrl('/login');
    });
  }
}
