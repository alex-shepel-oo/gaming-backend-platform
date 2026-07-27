import { TestBed } from '@angular/core/testing';
import { colorFor, initialsOf } from 'shared';
import { Avatar } from './avatar';

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
  it('renders the initials of the given name when no avatarUrl is set', () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Player One');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('PO');
    expect((fixture.nativeElement as HTMLElement).querySelector('img')).toBeNull();
  });

  it('renders an image when avatarUrl is set', () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Player One');
    fixture.componentRef.setInput('avatarUrl', 'https://example.com/avatar.png');
    fixture.detectChanges();

    const img = (fixture.nativeElement as HTMLElement).querySelector('img');
    expect(img).toBeTruthy();
    expect(img!.getAttribute('src')).toBe('https://example.com/avatar.png');
    expect(img!.getAttribute('alt')).toBe('Player One');
  });

  it('falls back to initials when avatarUrl is null', () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Player One');
    fixture.componentRef.setInput('avatarUrl', null);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('img')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('PO');
  });
});
