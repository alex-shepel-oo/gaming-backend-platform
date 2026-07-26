import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../economy/wallet.models';
import { IdentityUserEndpoints } from './identity-user-endpoints';

export interface UserSummary {
  id: string;
  email: string;
  displayName: string;
  role: string;
  createdAt: string;
}

export interface UserDetail {
  id: string;
  email: string;
  displayName: string;
  gameId: string | null;
  role: string | null;
  createdAt: string;
}

// Matches the backend's UserRoleDto exactly -- no email/displayName here,
// this is the assign-role response shape, not the user detail shape.
export interface UserRoleAssignment {
  userId: string;
  gameId: string | null;
  role: string;
  grantedAt: string;
}

@Injectable({ providedIn: 'root' })
export class UserManagementService {
  private readonly http = inject(HttpClient);

  listUsers(search: string | undefined, page: number, pageSize: number): Observable<PagedResult<UserSummary>> {
    return this.http.get<PagedResult<UserSummary>>(IdentityUserEndpoints.list(search, page, pageSize));
  }

  getUser(userId: string): Observable<UserDetail> {
    return this.http.get<UserDetail>(IdentityUserEndpoints.detail(userId));
  }

  assignRole(userId: string, gameId: string | undefined, role: string): Observable<UserRoleAssignment> {
    return this.http.patch<UserRoleAssignment>(IdentityUserEndpoints.role(userId), { gameId: gameId ?? null, role });
  }

  revokeSessions(userId: string, gameId?: string): Observable<void> {
    return this.http.post<void>(IdentityUserEndpoints.revokeSessions(userId, gameId), null);
  }
}
