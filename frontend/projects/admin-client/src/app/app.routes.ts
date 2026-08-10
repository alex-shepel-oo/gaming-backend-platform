import { Routes } from '@angular/router';
import { authGuard, guestGuard, NotFound, permissionGuard, roleGuard } from 'shared';
import { AdminLogin } from './login/admin-login';
import { AdminShell } from './shell/admin-shell';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  // AdminLogin is the screen every not-yet-authenticated visitor hits first, so it stays in the
  // initial bundle; every platform-admin screen behind it loads on demand.
  { path: 'login', component: AdminLogin, canActivate: [guestGuard], title: 'Sign in — GBP Admin' },
  {
    path: '',
    component: AdminShell,
    canActivate: [authGuard],
    children: [
      {
        path: 'select-game',
        loadComponent: () => import('./game-picker/game-picker').then((m) => m.GamePicker),
        title: 'Select game — GBP Admin',
      },
      {
        path: 'games',
        loadComponent: () => import('./games-management/games-management').then((m) => m.GamesManagement),
        canActivate: [permissionGuard],
        data: { permission: 'platform.games.manage' },
        title: 'Games — GBP Admin',
      },
      {
        path: 'roles',
        loadComponent: () =>
          import('./role-permissions-editor/role-permissions-editor').then((m) => m.RolePermissionsEditor),
        canActivate: [permissionGuard],
        data: { permission: 'platform.roles.manage' },
        title: 'Roles — GBP Admin',
      },
      {
        path: 'my-game',
        loadComponent: () => import('./my-game-metadata/my-game-metadata').then((m) => m.MyGameMetadata),
        canActivate: [permissionGuard],
        data: { permission: 'game.metadata.edit' },
        title: 'My Game — GBP Admin',
      },
      {
        path: 'users',
        loadComponent: () => import('./user-management/user-management').then((m) => m.UserManagement),
        canActivate: [roleGuard],
        data: { roles: ['Moderator', 'Admin'] },
        title: 'Users — GBP Admin',
      },
    ],
  },
  { path: '**', component: NotFound, title: 'Page not found — GBP Admin' },
];
