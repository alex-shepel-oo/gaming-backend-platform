import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { GameDetailsDialog } from './game-details-dialog';

describe('GameDetailsDialog', () => {
  function createWith(isSelected: boolean): { fixture: ComponentFixture<GameDetailsDialog>; dialogRefSpy: { close: ReturnType<typeof vi.fn> } } {
    const dialogRefSpy = { close: vi.fn() };

    TestBed.configureTestingModule({
      imports: [GameDetailsDialog],
      providers: [
        {
          provide: MAT_DIALOG_DATA,
          useValue: { game: { id: 'game-1', slug: 'demo-shooter', name: 'Demo Shooter' }, isSelected },
        },
        { provide: MatDialogRef, useValue: dialogRefSpy },
      ],
    });

    const fixture = TestBed.createComponent(GameDetailsDialog);
    fixture.detectChanges();

    return { fixture, dialogRefSpy };
  }

  it('shows the game name, slug, and a not-available note for the description', () => {
    const { fixture } = createWith(false);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Demo Shooter');
    expect(text).toContain('demo-shooter');
    expect(text).toContain("isn't available yet");
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
