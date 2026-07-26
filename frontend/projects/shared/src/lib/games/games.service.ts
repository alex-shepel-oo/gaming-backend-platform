import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { IdentityGameEndpoints } from './identity-game-endpoints';

export interface PublicGame {
  id: string;
  slug: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class GamesService {
  private readonly http = inject(HttpClient);

  listPublicGames(): Observable<PublicGame[]> {
    return this.http.get<PublicGame[]>(IdentityGameEndpoints.publicGames);
  }

  listMyGames(): Observable<PublicGame[]> {
    return this.http.get<PublicGame[]>(IdentityGameEndpoints.myGames);
  }
}
