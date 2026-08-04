import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { IdentityProfileEndpoints } from './identity-profile-endpoints';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  gameId: string | null;
  role: string | null;
  createdAt: string;
  avatarUrl: string | null;
  lastLoginAt: string | null;
}

export interface UpdateProfileRequest {
  displayName?: string;
  avatarUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);

  // Cross-screen snapshot (shell toolbar, Profile) so the avatar/displayName
  // shown in the toolbar and the profile screen stay in sync without each
  // independently re-fetching -- same posture as WalletService's balances.
  private readonly profileSignal = signal<UserProfile | null>(null);
  readonly profile = this.profileSignal.asReadonly();

  refreshProfile(): Observable<UserProfile> {
    return this.getMe().pipe(tap((profile) => this.profileSignal.set(profile)));
  }

  clearProfile(): void {
    this.profileSignal.set(null);
  }

  getMe(): Observable<UserProfile> {
    return this.http.get<UserProfile>(IdentityProfileEndpoints.me);
  }

  updateMe(request: UpdateProfileRequest): Observable<UserProfile> {
    return this.http
      .patch<UserProfile>(IdentityProfileEndpoints.me, request)
      .pipe(tap((profile) => this.profileSignal.set(profile)));
  }
}
