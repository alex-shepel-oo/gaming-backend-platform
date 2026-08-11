import { TestBed } from '@angular/core/testing';
import { SelectDropdown, SelectDropdownOption } from './select-dropdown';

const OPTIONS: SelectDropdownOption[] = [
  { id: 'a', label: 'Alpha' },
  { id: 'b', label: 'Bravo' },
];

describe('SelectDropdown', () => {
  it('shows the label of the option matching the written value', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentInstance.writeValue('b');
    fixture.detectChanges();

    const trigger = (fixture.nativeElement as HTMLElement).querySelector('.select-dropdown__trigger');
    expect(trigger?.textContent?.trim()).toContain('Bravo');
  });

  it('opens the option list on trigger click and reports the trigger width as the popup width', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!;
    vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue({ width: 212 } as DOMRect);

    trigger.click();
    fixture.detectChanges();

    const popup = element.querySelector<HTMLElement>('.select-dropdown__popup');
    expect(popup).not.toBeNull();
    expect(popup?.style.width).toBe('212px');
    expect(popup?.querySelectorAll('.select-dropdown__option')).toHaveLength(2);
  });

  it('selecting an option reports the value via ControlValueAccessor and closes the popup', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();

    const onChange = vi.fn();
    fixture.componentInstance.registerOnChange(onChange);

    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!.click();
    fixture.detectChanges();

    const options = element.querySelectorAll<HTMLElement>('.select-dropdown__option');
    options[1].click();
    fixture.detectChanges();

    expect(onChange).toHaveBeenCalledWith('b');
    expect(element.querySelector('.select-dropdown__popup')).toBeNull();
  });

  it('does not open when readonlyMode is set', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentRef.setInput('readonlyMode', true);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!.click();
    fixture.detectChanges();

    expect(element.querySelector('.select-dropdown__popup')).toBeNull();
  });

  it('closes on Escape', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!;
    trigger.click();
    fixture.detectChanges();
    expect(element.querySelector('.select-dropdown__popup')).not.toBeNull();

    trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(element.querySelector('.select-dropdown__popup')).toBeNull();
  });

  it('closes when a click lands outside the component', () => {
    const fixture = TestBed.createComponent(SelectDropdown);
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('.select-dropdown__trigger')!.click();
    fixture.detectChanges();
    expect(element.querySelector('.select-dropdown__popup')).not.toBeNull();

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(element.querySelector('.select-dropdown__popup')).toBeNull();
  });
});
