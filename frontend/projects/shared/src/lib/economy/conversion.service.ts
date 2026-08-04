import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Conversion, ConversionRateDto, ConvertRequest } from './conversion.models';
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

  rate(fromCurrencyId: string, toCurrencyId: string): Observable<ConversionRateDto> {
    const params = new HttpParams().set('fromCurrencyId', fromCurrencyId).set('toCurrencyId', toCurrencyId);

    return this.http.get<ConversionRateDto>(EconomyEndpoints.conversionRate, { params });
  }
}
