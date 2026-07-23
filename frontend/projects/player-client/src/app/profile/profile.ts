import { Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { TokenStore } from 'shared';
import { Avatar } from '../ui/avatar/avatar';
import { NotAvailable } from '../ui/not-available/not-available';

@Component({
  selector: 'app-profile',
  imports: [MatCardModule, Avatar, NotAvailable],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile {
  protected readonly tokenStore = inject(TokenStore);
}
