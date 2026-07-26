import { Routes } from '@angular/router';
import { authGuard, guestGuard } from 'shared';
import { AdminDashboard } from './dashboard/admin-dashboard';
import { GamePicker } from './game-picker/game-picker';
import { AdminLogin } from './login/admin-login';
import { AdminShell } from './shell/admin-shell';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: AdminLogin, canActivate: [guestGuard] },
  {
    path: '',
    component: AdminShell,
    canActivate: [authGuard],
    children: [
      { path: 'select-game', component: GamePicker },
      { path: 'dashboard', component: AdminDashboard },
    ],
  },
  { path: '**', redirectTo: '' },
];
