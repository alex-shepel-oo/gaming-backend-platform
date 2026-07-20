import { Routes } from '@angular/router';
import { authGuard } from 'shared';
import { Convert } from './convert/convert';
import { Games } from './games/games';
import { Login } from './login/login';
import { Wallet } from './wallet/wallet';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: Login },
  { path: 'games', component: Games, canActivate: [authGuard] },
  { path: 'wallet', component: Wallet, canActivate: [authGuard] },
  { path: 'convert', component: Convert, canActivate: [authGuard] },
];
