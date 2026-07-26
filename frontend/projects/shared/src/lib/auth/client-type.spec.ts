import { TestBed } from '@angular/core/testing';
import { CLIENT_TYPE } from './client-type';

describe('CLIENT_TYPE', () => {
  it('defaults to "web" when no app provides a value', () => {
    TestBed.configureTestingModule({});

    expect(TestBed.inject(CLIENT_TYPE)).toBe('web');
  });

  it('takes an app-provided value over the default', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: CLIENT_TYPE, useValue: 'admin' }],
    });

    expect(TestBed.inject(CLIENT_TYPE)).toBe('admin');
  });
});
