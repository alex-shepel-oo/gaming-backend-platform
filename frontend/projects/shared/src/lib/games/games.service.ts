import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { IdentityGameEndpoints } from './identity-game-endpoints';

export interface PublicGame {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  iconUrl: string | null;
}

// The platform-admin shape (GameDto on the backend) -- distinct from
// PublicGame, which is the narrower, public-facing shape returned by
// /games/public. Don't conflate the two: PublicGame is what a player picks
// from, Game is what platform.games.manage lets an admin CRUD.
export interface Game {
  id: string;
  slug: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  description: string | null;
  iconUrl: string | null;
}

export interface CreateGameRequest {
  slug: string;
  name: string;
}

// Empty string clears the field back to null; omitting a field leaves it
// unchanged -- same PATCH convention as UpdateProfileRequest on the backend.
export interface UpdateGameRequest {
  name?: string;
  isActive?: boolean;
  description?: string;
  iconUrl?: string;
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

  listAllGames(): Observable<Game[]> {
    return this.http.get<Game[]>(IdentityGameEndpoints.allGames);
  }

  createGame(request: CreateGameRequest): Observable<Game> {
    return this.http.post<Game>(IdentityGameEndpoints.allGames, request);
  }

  updateGame(id: string, request: UpdateGameRequest): Observable<Game> {
    return this.http.patch<Game>(IdentityGameEndpoints.game(id), request);
  }
}
