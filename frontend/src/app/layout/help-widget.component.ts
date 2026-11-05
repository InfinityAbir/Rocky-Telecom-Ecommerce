import { NgTemplateOutlet } from '@angular/common';
import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChromeService } from '../core/chrome.service';

const STORE_PHONE = '01700000000';
const SEEN_KEY = 'rt_help_greeted';

/**
 * Floating help button (bottom right): a friendly greeting, a small panel of ways to reach the shop,
 * and a scroll-to-top button. It sits above the mobile bottom nav and any page-level purchase bar.
 */
@Component({
  selector: 'rt-help-widget',
  standalone: true,
  imports: [RouterLink, NgTemplateOutlet],
  template: `
    <div class="dock" [style.--lift.px]="chrome.bottomInset()">
      @if (showTop()) {
        <button class="top" type="button" (click)="toTop()" aria-label="Back to top">
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d="M6 15l6-6 6 6" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </button>
      }

      @if (open()) {
        <div class="panel" role="dialog" aria-label="Contact Rocky Telecom">
          <div class="head">
            <div class="who">
              <span class="face sm"><ng-container *ngTemplateOutlet="avatar" /></span>
              <div>
                <strong>Rocky Telecom support</strong>
                <div class="tiny"><span class="dot"></span> Online · replies in minutes</div>
              </div>
            </div>
            <button type="button" class="x" (click)="open.set(false)" aria-label="Close help">✕</button>
          </div>

          <div class="body">
            <p class="bubble-msg">আসসালামু আলাইকুম! কিভাবে সাহায্য করতে পারি?<br /><span class="muted small">Hello! How can we help you today?</span></p>

            <a class="act" [href]="'tel:' + phone">📞 <span>Call the showroom <small>01700-000000</small></span></a>
            <a class="act" [href]="'https://wa.me/88' + phone" target="_blank" rel="noopener noreferrer">💬 <span>Chat on WhatsApp <small>Quick answers</small></span></a>
            <a class="act" routerLink="/support" (click)="open.set(false)">✉️ <span>Send us a message <small>We reply by phone or SMS</small></span></a>
            <a class="act" routerLink="/track" (click)="open.set(false)">📦 <span>Track my order</span></a>
            <a class="act" routerLink="/shop" (click)="open.set(false)">🔎 <span>Find the right phone</span></a>
          </div>
        </div>
      }

      <div class="row">
        @if (greeting() && !open()) {
          <button type="button" class="greet" (click)="openPanel()">
            স্যার, কিভাবে সহযোগিতা করতে পারি?
          </button>
        }
        <button type="button" class="fab" (click)="open() ? open.set(false) : openPanel()" [attr.aria-expanded]="open()" aria-label="Chat with us">
          <span class="face"><ng-container *ngTemplateOutlet="avatar" /></span>
          <span class="dot big" aria-hidden="true"></span>
        </button>
      </div>
    </div>

    <!-- Original illustration: a friendly support person with a headset. -->
    <ng-template #avatar>
      <svg viewBox="0 0 64 64" width="100%" height="100%" role="img" aria-label="Support agent">
        <defs>
          <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f6c9a5" /><stop offset="1" stop-color="#e9a07a" /></linearGradient>
        </defs>
        <rect width="64" height="64" fill="#ffe9c7" />
        <path d="M8 64c2-14 12-20 24-20s22 6 24 20z" fill="#e0491f" />
        <path d="M26 44h12v6a6 6 0 0 1-12 0z" fill="url(#bg)" />
        <ellipse cx="32" cy="30" rx="12" ry="14" fill="url(#bg)" />
        <path d="M19 28c-1-13 7-19 14-19s14 5 12 19c-2-6-6-9-13-9s-11 3-13 9z" fill="#2a1b12" />
        <path d="M17 30a15 15 0 0 1 30 0" fill="none" stroke="#201a13" stroke-width="2.6" stroke-linecap="round" />
        <rect x="13.5" y="28" width="6" height="10" rx="3" fill="#201a13" />
        <rect x="44.5" y="28" width="6" height="10" rx="3" fill="#201a13" />
        <path d="M47 37c0 6-5 9-11 9" fill="none" stroke="#201a13" stroke-width="2" stroke-linecap="round" />
        <circle cx="27" cy="31" r="1.5" fill="#201a13" />
        <circle cx="37" cy="31" r="1.5" fill="#201a13" />
        <path d="M27.5 37c2.5 2.6 6.5 2.6 9 0" fill="none" stroke="#7a2e1c" stroke-width="1.8" stroke-linecap="round" />
      </svg>
    </ng-template>
  `,
  styles: [
    `
      :host { position: fixed; right: 16px; bottom: 0; z-index: 48; }
      .dock {
        display: grid; justify-items: end; gap: 12px;
        margin-bottom: calc(24px + var(--lift, 0px));
      }
      @media (max-width: 1023px) {
        /* Clear the 56px bottom nav. */
        .dock { margin-bottom: calc(72px + env(safe-area-inset-bottom) + var(--lift, 0px)); }
      }

      .row { display: flex; align-items: center; gap: 10px; }

      .fab {
        position: relative; width: 60px; height: 60px; padding: 0; border: 2px solid var(--ink);
        border-radius: 50%; background: var(--cream); cursor: pointer; box-shadow: 3px 3px 0 var(--ink);
        transition: transform 0.15s ease;
      }
      .fab:hover { transform: translate(-1px, -1px); }
      .fab:focus-visible, .top:focus-visible, .greet:focus-visible { outline: 3px solid var(--rust); outline-offset: 3px; }
      .face { display: block; width: 100%; height: 100%; border-radius: 50%; overflow: hidden; }
      .face.sm { width: 38px; height: 38px; flex: none; border: 2px solid var(--ink); }
      .dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; background: #1fa34a; margin-right: 4px; }
      .dot.big { position: absolute; top: -2px; right: -2px; width: 16px; height: 16px; margin: 0; border: 2px solid var(--cream); }

      .greet {
        max-width: 250px; padding: 11px 16px; border: 2px solid var(--ink); border-radius: 999px;
        background: var(--cream); color: var(--ink); font: inherit; font-size: 14px; font-weight: 600;
        cursor: pointer; box-shadow: 2px 2px 0 var(--ink); animation: pop 0.3s ease-out;
      }
      @keyframes pop { from { opacity: 0; transform: translateX(10px); } to { opacity: 1; transform: none; } }
      @media (max-width: 420px) { .greet { display: none; } }

      .top {
        width: 48px; height: 48px; display: grid; place-items: center; border-radius: 50%;
        border: 2px solid var(--ink); background: var(--cream); color: var(--ink); cursor: pointer;
        box-shadow: 2px 2px 0 var(--ink); margin-right: 6px;
      }

      .panel {
        width: min(340px, calc(100vw - 32px)); background: var(--paper); border: 2px solid var(--ink);
        border-radius: 16px; box-shadow: 4px 4px 0 var(--ink); overflow: hidden; animation: pop 0.2s ease-out;
      }
      .head { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 12px 14px; background: var(--ink); color: var(--cream); }
      .who { display: flex; gap: 10px; align-items: center; }
      .x { background: none; border: 0; color: inherit; font-size: 16px; cursor: pointer; min-width: 44px; min-height: 44px; }
      .body { padding: 14px; display: grid; gap: 8px; }
      .bubble-msg { margin: 0 0 4px; padding: 10px 12px; background: var(--cream); border: 2px solid var(--ink); border-radius: 12px 12px 12px 2px; }
      .act {
        display: flex; align-items: center; gap: 10px; min-height: 48px; padding: 6px 12px;
        border: 2px solid var(--ink); border-radius: 10px; background: var(--cream); color: var(--ink);
        text-decoration: none; font-weight: 700;
      }
      .act small { display: block; font-weight: 500; color: var(--ink-soft); }
      .act:hover, .act:focus-visible { background: var(--mustard); outline: none; }
    `
  ]
})
export class HelpWidgetComponent implements OnInit {
  readonly chrome = inject(ChromeService);
  readonly phone = STORE_PHONE;

  readonly open = signal(false);
  readonly greeting = signal(false);
  readonly showTop = signal(false);

  ngOnInit(): void {
    if (typeof window === 'undefined') {
      return;
    }

    // Greet once per visit, a few seconds in, so it never fights the first paint.
    let seen = false;
    try {
      seen = sessionStorage.getItem(SEEN_KEY) === '1';
    } catch {
      /* storage can be blocked; greeting then just shows */
    }

    if (!seen) {
      setTimeout(() => {
        this.greeting.set(true);
        try {
          sessionStorage.setItem(SEEN_KEY, '1');
        } catch {
          /* ignore */
        }
        setTimeout(() => this.greeting.set(false), 12000);
      }, 2500);
    }
  }

  openPanel(): void {
    this.greeting.set(false);
    this.open.set(true);
  }

  toTop(): void {
    window.scrollTo({ top: 0, behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }

  @HostListener('window:scroll')
  onScroll(): void {
    this.showTop.set(typeof window !== 'undefined' && window.scrollY > 500);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.open.set(false);
  }
}
