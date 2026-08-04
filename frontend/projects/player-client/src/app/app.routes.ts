import { Routes } from '@angular/router';
import { authGuard, guestGuard } from 'shared';
import { AuthShell } from './auth-shell/auth-shell';
import { Shell } from './shell/shell';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  // AuthShell is the screen every not-yet-authenticated visitor hits first, so it stays in the
  // initial bundle; everything past it loads on demand.
  { path: 'login', component: AuthShell, canActivate: [guestGuard] },
  { path: 'reset-password', loadComponent: () => import('./reset-password/reset-password').then((m) => m.ResetPassword) },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: 'games', loadComponent: () => import('./games/games').then((m) => m.Games) },
      { path: 'wallet', loadComponent: () => import('./wallet/wallet').then((m) => m.Wallet) },
      { path: 'convert', loadComponent: () => import('./convert/convert').then((m) => m.Convert) },
      { path: 'profile', loadComponent: () => import('./profile/profile').then((m) => m.Profile) },
    ],
  },
  { path: '**', redirectTo: '' },
];
