import { Component, inject } from '@angular/core';
import { TokenStore } from 'shared';

// Placeholder landing route -- real dashboard content (games CRUD, permission
// catalog, user management) lands in later sessions of this group. All this
// needs to prove today is that a signed-in admin/moderator actually reaches
// somewhere usable after login or after picking a game.
@Component({
  selector: 'admin-dashboard',
  templateUrl: './admin-dashboard.html',
  styleUrl: './admin-dashboard.scss',
})
export class AdminDashboard {
  protected readonly tokenStore = inject(TokenStore);
}
