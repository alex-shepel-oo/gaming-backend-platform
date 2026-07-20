import { Routes } from '@angular/router';
import { authGuard } from 'shared';
import { Games } from './games/games';
import { Login } from './login/login';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: Login },
  { path: 'games', component: Games, canActivate: [authGuard] },
];
