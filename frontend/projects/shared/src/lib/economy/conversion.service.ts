import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Conversion, ConvertRequest } from './conversion.models';
import { EconomyEndpoints } from './economy-endpoints';

@Injectable({ providedIn: 'root' })
export class ConversionService {
  private readonly http = inject(HttpClient);

  create(request: ConvertRequest, idempotencyKey: string): Observable<Conversion> {
    return this.http.post<Conversion>(EconomyEndpoints.conversions, request, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  get(conversionId: string): Observable<Conversion> {
    return this.http.get<Conversion>(EconomyEndpoints.conversion(conversionId));
  }
}
