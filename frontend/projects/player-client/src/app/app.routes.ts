import { Routes } from '@angular/router';
import { authGuard, gameScopeGuard, guestGuard } from 'shared';
import { AuthShell } from './auth-shell/auth-shell';
import { Convert } from './convert/convert';
import { Games } from './games/games';
import { Profile } from './profile/profile';
import { ResetPassword } from './reset-password/reset-password';
import { Shell } from './shell/shell';
import { Wallet } from './wallet/wallet';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: AuthShell, canActivate: [guestGuard] },
  { path: 'reset-password', component: ResetPassword },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: 'games', component: Games },
      { path: 'wallet', component: Wallet, canActivate: [gameScopeGuard] },
      { path: 'convert', component: Convert, canActivate: [gameScopeGuard] },
      { path: 'profile', component: Profile, canActivate: [gameScopeGuard] },
    ],
  },
  { path: '**', redirectTo: '' },
];
