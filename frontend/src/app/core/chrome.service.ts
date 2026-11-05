import { Injectable, signal } from '@angular/core';

/**
 * Space that page-level fixed bars (for example the product page's purchase bar) take at the bottom of the
 * screen, so floating controls such as the help button sit above them instead of covering them.
 */
@Injectable({ providedIn: 'root' })
export class ChromeService {
  readonly bottomInset = signal(0);
}
