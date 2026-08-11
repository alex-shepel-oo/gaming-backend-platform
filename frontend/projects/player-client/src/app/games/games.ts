import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { map } from 'rxjs/operators';
import { Balance, CurrencyScope, GamesService, NotAvailable, PageBackground, PublicGame, WalletService } from 'shared';
import { GameDetailsDialog } from './game-details-dialog/game-details-dialog';

// Matches the site's own mobile breakpoint (shell.scss's bottom-nav switch).
// Below this, cards are too small to show balance/description inline, so
// that info moves into a tap-to-open dialog instead.
const MOBILE_BREAKPOINT = '(max-width: 639px)';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-games',
  imports: [MatIconModule, MatProgressSpinnerModule, NotAvailable, PageBackground],
  templateUrl: './games.html',
  styleUrl: './games.scss',
})
export class Games {
  private readonly gamesService = inject(GamesService);
  private readonly walletService = inject(WalletService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpointObserver = inject(BreakpointObserver);

  protected readonly games = signal<PublicGame[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  // Only mobile cards open the info dialog; desktop cards show the same
  // balance/description directly, so there's nothing for a click to do there.
  protected readonly isMobile = toSignal(
    this.breakpointObserver.observe(MOBILE_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  // Zero-amount balances are dropped: a currency the player doesn't
  // actually hold isn't worth a badge (see Wallet's identical filter on
  // gameBalanceGroups).
  private readonly gameBalances = computed(() => {
    const byGame = new Map<string, Balance[]>();

    for (const balance of this.walletService.balances() ?? []) {
      if (balance.scope !== CurrencyScope.Game || balance.gameId === null || balance.amount <= 0) {
        continue;
      }

      const existing = byGame.get(balance.gameId);
      if (existing) {
        existing.push(balance);
      } else {
        byGame.set(balance.gameId, [balance]);
      }
    }

    return byGame;
  });

  // See Wallet's identical failedIcons: a gameId lands here once its
  // iconUrl fails to load, so the broken-image glyph never shows.
  protected readonly failedIcons = signal<ReadonlySet<string>>(new Set());

  protected onIconError(gameId: string): void {
    this.failedIcons.update((failed) => new Set(failed).add(gameId));
  }

  constructor() {
    this.walletService.refreshBalances().subscribe();

    this.gamesService.listPublicGames().subscribe({
      next: (games) => {
        this.games.set(games);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  protected balancesFor(gameId: string): Balance[] {
    return this.gameBalances().get(gameId) ?? [];
  }

  protected onCardActivate(game: PublicGame): void {
    if (!this.isMobile()) {
      return;
    }

    this.dialog.open(GameDetailsDialog, {
      data: { game, balances: this.balancesFor(game.id) },
      width: '420px',
      enterAnimationDuration: '0ms',
      exitAnimationDuration: '0ms',
    });
  }
}
