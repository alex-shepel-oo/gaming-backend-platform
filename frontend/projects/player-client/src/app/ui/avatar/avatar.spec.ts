import { TestBed } from '@angular/core/testing';
import { Avatar, colorFor, initialsOf } from './avatar';

describe('initialsOf', () => {
  it('takes the first letter of up to two words, uppercased', () => {
    expect(initialsOf('Player One')).toBe('PO');
    expect(initialsOf('demo')).toBe('D');
    expect(initialsOf('  three word name ')).toBe('TW');
  });
});

describe('colorFor', () => {
  it('is deterministic for the same name', () => {
    expect(colorFor('Player One')).toBe(colorFor('Player One'));
  });

  it('differs for different names', () => {
    expect(colorFor('Player One')).not.toBe(colorFor('Player Two'));
  });
});

describe('Avatar', () => {
  it('renders the initials of the given name', () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Player One');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('PO');
  });
});
