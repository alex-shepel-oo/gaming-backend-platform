import { Routes } from '@angular/router';
import { authGuard, guestGuard, NotFound } from 'shared';
import { AuthShell } from './auth-shell/auth-shell';
import { Landing } from './landing/landing';
import { Shell } from './shell/shell';

export const routes: Routes = [
  // Landing and AuthShell are the two screens every not-yet-authenticated visitor can land on
  // first, so both stay in the initial bundle; everything past them loads on demand.
  { path: '', pathMatch: 'full', component: Landing, canActivate: [guestGuard], title: 'GBP Home' },
  { path: 'login', component: AuthShell, canActivate: [guestGuard], title: 'Sign in — GBP Player' },
  {
    path: 'reset-password',
    loadComponent: () => import('./reset-password/reset-password').then((m) => m.ResetPassword),
    title: 'Reset password — GBP Player',
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: 'games', loadComponent: () => import('./games/games').then((m) => m.Games), title: 'Games — GBP Player' },
      { path: 'wallet', loadComponent: () => import('./wallet/wallet').then((m) => m.Wallet), title: 'Wallet — GBP Player' },
      {
        path: 'convert',
        loadComponent: () => import('./convert/convert').then((m) => m.Convert),
        title: 'Convert — GBP Player',
      },
      {
        path: 'profile',
        loadComponent: () => import('./profile/profile').then((m) => m.Profile),
        title: 'Profile — GBP Player',
      },
    ],
  },
  { path: '**', component: NotFound, title: 'Page not found — GBP Player' },
];
