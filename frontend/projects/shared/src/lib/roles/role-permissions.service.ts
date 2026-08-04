import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { IdentityRoleEndpoints } from './identity-role-endpoints';

@Injectable({ providedIn: 'root' })
export class RolePermissionsService {
  private readonly http = inject(HttpClient);

  getPermissionCatalog(): Observable<string[]> {
    return this.http.get<string[]>(IdentityRoleEndpoints.permissionCatalog);
  }

  getRolePermissions(role: string, gameId?: string): Observable<string[]> {
    return this.http.get<string[]>(IdentityRoleEndpoints.rolePermissions(role, gameId));
  }

  updateRolePermissions(role: string, permissions: string[], gameId?: string): Observable<string[]> {
    return this.http.put<string[]>(IdentityRoleEndpoints.rolePermissions(role, gameId), { permissions });
  }
}
