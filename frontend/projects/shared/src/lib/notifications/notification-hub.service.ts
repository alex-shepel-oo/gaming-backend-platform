import { Injectable, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { TokenStore } from '../auth/token-store';
import { WalletService } from '../economy/wallet.service';
import { BalanceChangedMessage } from './balance-changed-message';

const NOTIFICATION_HUB_URL = '/hubs/notifications';

@Injectable({ providedIn: 'root' })
export class NotificationHubService {
  private readonly tokenStore = inject(TokenStore);
  private readonly walletService = inject(WalletService);

  private connection: HubConnection | null = null;

  connect(): void {
    const connection = new HubConnectionBuilder()
      .withUrl(NOTIFICATION_HUB_URL, { accessTokenFactory: () => this.tokenStore.read() ?? '' })
      .withAutomaticReconnect()
      .build();

    connection.on('balanceChanged', (message: BalanceChangedMessage) => {
      this.walletService.applyBalanceChange(message.currencyId, message.balance);
    });

    this.connection = connection;
    void connection.start();
  }

  disconnect(): void {
    void this.connection?.stop();
  }
}
