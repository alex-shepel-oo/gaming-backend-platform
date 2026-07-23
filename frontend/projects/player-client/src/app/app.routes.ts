import { Routes } from '@angular/router';
import { authGuard } from 'shared';
import { AuthShell } from './auth-shell/auth-shell';
import { Convert } from './convert/convert';
import { Games } from './games/games';
import { Shell } from './shell/shell';
import { Wallet } from './wallet/wallet';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: AuthShell },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: 'games', component: Games },
      { path: 'wallet', component: Wallet },
      { path: 'convert', component: Convert },
    ],
  },
];
