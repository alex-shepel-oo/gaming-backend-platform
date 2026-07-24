import { Routes } from '@angular/router';
import { authGuard, guestGuard } from 'shared';
import { AuthShell } from './auth-shell/auth-shell';
import { Convert } from './convert/convert';
import { Games } from './games/games';
import { Profile } from './profile/profile';
import { Shell } from './shell/shell';
import { Wallet } from './wallet/wallet';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: AuthShell, canActivate: [guestGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: 'games', component: Games },
      { path: 'wallet', component: Wallet },
      { path: 'convert', component: Convert },
      { path: 'profile', component: Profile },
    ],
  },
  { path: '**', redirectTo: '' },
];
