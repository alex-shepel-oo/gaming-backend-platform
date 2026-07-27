import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, roleGuard } from 'shared';
import { AdminDashboard } from './dashboard/admin-dashboard';
import { GamePicker } from './game-picker/game-picker';
import { GamesManagement } from './games-management/games-management';
import { AdminLogin } from './login/admin-login';
import { MyGameMetadata } from './my-game-metadata/my-game-metadata';
import { RolePermissionsEditor } from './role-permissions-editor/role-permissions-editor';
import { AdminShell } from './shell/admin-shell';
import { UserManagement } from './user-management/user-management';

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
      {
        path: 'games',
        component: GamesManagement,
        canActivate: [permissionGuard],
        data: { permission: 'platform.games.manage' },
      },
      {
        path: 'roles',
        component: RolePermissionsEditor,
        canActivate: [permissionGuard],
        data: { permission: 'platform.roles.manage' },
      },
      {
        path: 'my-game',
        component: MyGameMetadata,
        canActivate: [permissionGuard],
        data: { permission: 'game.metadata.edit' },
      },
      {
        path: 'users',
        component: UserManagement,
        canActivate: [roleGuard],
        data: { roles: ['Moderator', 'Admin'] },
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
