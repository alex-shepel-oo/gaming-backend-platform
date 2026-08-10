import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CurrencyScope, GameSelectionService, NotificationHubService, ProfileService, TokenStore, WalletService } from 'shared';
import { Avatar } from '../ui/avatar/avatar';
import { ThemeService } from '../theme/theme.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, MatIconModule, Avatar],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly walletService = inject(WalletService);
  private readonly notificationHub = inject(NotificationHubService);

  // No toggle button reads this anymore (moved off the header on request),
  // but injecting ThemeService is what starts its constructor-side effect
  // that syncs document.documentElement's color-scheme. Without an injector
  // reaching it from somewhere, the light/dark auto-detection logic would
  // never run at all.
  protected readonly theme = inject(ThemeService);

  protected readonly gameSelection = inject(GameSelectionService);
  protected readonly tokenStore = inject(TokenStore);
  protected readonly profileService = inject(ProfileService);

  protected readonly platformBalances = computed(
    () => this.walletService.balances()?.filter((balance) => balance.scope === CurrencyScope.Platform) ?? [],
  );

  // See Wallet's identical failedIcons: a currencyId lands here once its
  // iconUrl fails to load, so the broken-image glyph never shows.
  protected readonly failedIcons = signal<ReadonlySet<string>>(new Set());

  protected onIconError(currencyId: string): void {
    this.failedIcons.update((failed) => new Set(failed).add(currencyId));
  }

  constructor() {
    this.walletService.refreshBalances().subscribe();
    this.profileService.refreshProfile().subscribe();
    this.notificationHub.connect();
  }
}
