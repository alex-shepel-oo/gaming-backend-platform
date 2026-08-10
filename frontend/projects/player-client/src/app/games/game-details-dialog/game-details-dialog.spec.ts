import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Balance, CurrencyScope } from 'shared';
import { GameDetailsDialog } from './game-details-dialog';

describe('GameDetailsDialog', () => {
  function createWith(
    balances: Balance[],
    gameOverrides: Partial<{ description: string | null; iconUrl: string | null }> = {},
  ): { fixture: ComponentFixture<GameDetailsDialog> } {
    TestBed.configureTestingModule({
      imports: [GameDetailsDialog],
      providers: [
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            game: {
              id: 'game-1',
              slug: 'demo-shooter',
              name: 'Demo Shooter',
              description: null,
              iconUrl: null,
              ...gameOverrides,
            },
            balances,
          },
        },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
      ],
    });

    const fixture = TestBed.createComponent(GameDetailsDialog);
    fixture.detectChanges();

    return { fixture };
  }

  it('shows the game name and slug, and hides the balance section entirely when there is none', () => {
    const { fixture } = createWith([]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
    expect(text).toContain('demo-shooter');
    expect(text).not.toContain('Balance in this game');
    expect(text).toContain("isn't available yet");
  });

  it('shows the real description and balance when the game has them', () => {
    const { fixture } = createWith(
      [{ currencyId: 'currency-1', currencyCode: 'SHOOTER_GOLD', scope: CurrencyScope.Game, gameId: 'game-1', amount: 40, iconUrl: null }],
      { description: 'A fast-paced arena shooter.' },
    );

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('A fast-paced arena shooter.');
    expect(text).not.toContain("isn't available yet");
    expect(text).toContain('Balance in this game');
    expect(text).toContain('40 SHOOTER_GOLD');
  });

  it('shows the game icon when one is set', () => {
    const { fixture } = createWith([], { iconUrl: 'https://example.test/icon.png' });

    const icon = (fixture.nativeElement as HTMLElement).querySelector('.game-details-icon') as HTMLImageElement | null;
    expect(icon).not.toBeNull();
    expect(icon?.src).toBe('https://example.test/icon.png');
  });

  it('falls back to the placeholder icon when the image fails to load', () => {
    const { fixture } = createWith([], { iconUrl: 'https://example.test/broken.png' });

    const element = fixture.nativeElement as HTMLElement;
    const image = element.querySelector('img.game-details-icon') as HTMLImageElement;
    expect(image).not.toBeNull();

    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(element.querySelector('img.game-details-icon')).toBeNull();
    expect(element.querySelector('.game-details-icon-placeholder')).not.toBeNull();
  });

  it('shows a placeholder icon instead of an image when the game has none', () => {
    const { fixture } = createWith([]);

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('img.game-details-icon')).toBeNull();
    expect(element.querySelector('.game-details-icon-placeholder')).not.toBeNull();
  });

  it('has no select action, only a Close button', () => {
    const { fixture } = createWith([]);

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map((button) =>
      button.textContent?.trim(),
    );
    expect(buttons).toEqual(['Close']);
  });
});
