import { Injectable, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
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
    // A prior connection left open (e.g. a fast logout/login cycle racing
    // disconnect()'s own fire-and-forget stop()) would otherwise leak here.
    // Every connect() first closes whatever it's replacing.
    if (this.connection) {
      void this.connection.stop();
    }

    const connection = new HubConnectionBuilder()
      .withUrl(NOTIFICATION_HUB_URL, { accessTokenFactory: () => this.tokenStore.read() ?? '' })
      .withAutomaticReconnect()
      // Default Information-level logging writes the connection URL,
      // including the access token SignalR appends as a query param (the
      // only way to authenticate a native WebSocket handshake), straight to
      // the browser console. Warning still surfaces real connection
      // problems without leaking the token.
      .configureLogging(LogLevel.Warning)
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
