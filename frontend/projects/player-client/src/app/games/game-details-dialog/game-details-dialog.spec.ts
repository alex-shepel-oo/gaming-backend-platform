import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { GameDetailsDialog } from './game-details-dialog';

describe('GameDetailsDialog', () => {
  function createWith(
    isSelected: boolean,
    gameOverrides: Partial<{ description: string | null; iconUrl: string | null }> = {},
  ): { fixture: ComponentFixture<GameDetailsDialog>; dialogRefSpy: { close: ReturnType<typeof vi.fn> } } {
    const dialogRefSpy = { close: vi.fn() };

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
            isSelected,
          },
        },
        { provide: MatDialogRef, useValue: dialogRefSpy },
      ],
    });

    const fixture = TestBed.createComponent(GameDetailsDialog);
    fixture.detectChanges();

    return { fixture, dialogRefSpy };
  }

  it('shows the game name, slug, and a not-available note when the description is null', () => {
    const { fixture } = createWith(false);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
    expect(text).toContain('demo-shooter');
    expect(text).toContain("isn't available yet");
  });

  it('shows the real description when the game has one', () => {
    const { fixture } = createWith(false, { description: 'A fast-paced arena shooter.' });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('A fast-paced arena shooter.');
    expect(text).not.toContain("isn't available yet");
  });

  it('shows the game icon when one is set', () => {
    const { fixture } = createWith(false, { iconUrl: 'https://example.test/icon.png' });

    const icon = (fixture.nativeElement as HTMLElement).querySelector('.game-details-icon') as HTMLImageElement | null;
    expect(icon).not.toBeNull();
    expect(icon?.src).toBe('https://example.test/icon.png');
  });

  it('shows no icon element when the game has none', () => {
    const { fixture } = createWith(false);

    const icon = (fixture.nativeElement as HTMLElement).querySelector('.game-details-icon');
    expect(icon).toBeNull();
  });

  it('closes with "select" when the select button is clicked', () => {
    const { fixture, dialogRefSpy } = createWith(false);

    const selectButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (button) => button.textContent?.includes('Select this game'),
    )!;
    selectButton.dispatchEvent(new Event('click'));

    expect(dialogRefSpy.close).toHaveBeenCalledWith('select');
  });

  it('disables the select button when the game is already selected', () => {
    const { fixture } = createWith(true);

    const selectButton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (button) => button.textContent?.includes('Selected'),
    ) as HTMLButtonElement;

    expect(selectButton.disabled).toBe(true);
  });
});
