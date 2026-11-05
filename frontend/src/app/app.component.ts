import { Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import {
  BottomNavComponent,
  FooterComponent,
  HeaderComponent,
  ToastHostComponent,
  TopStripComponent
} from './layout/layout.components';
import { CategoryDrawerComponent } from './layout/category-nav.component';
import { HelpWidgetComponent } from './layout/help-widget.component';
import { SeoService } from './core/seo.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet,
    TopStripComponent,
    HeaderComponent,
    FooterComponent,
    BottomNavComponent,
    CategoryDrawerComponent,
    HelpWidgetComponent,
    ToastHostComponent
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private readonly router = inject(Router);
  private readonly seo = inject(SeoService);

  constructor() {
    // Every navigation starts from the site defaults; pages then set their own tags once data loads.
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed()
      )
      .subscribe((event) => {
        const [path] = event.urlAfterRedirects.split('?');
        this.seo.reset(path, path.startsWith('/admin') || this.routeIsNoindex());
      });
  }

  /** True when the active route (or a parent) is marked `data: { noindex: true }`. */
  private routeIsNoindex(): boolean {
    let route: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;

    while (route) {
      if (route.data?.['noindex']) {
        return true;
      }

      route = route.firstChild;
    }

    return false;
  }

  /** Storefront chrome is suppressed in the admin area — it has its own navigation. */
  readonly isAdmin = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects.startsWith('/admin')),
      startWith(this.router.url.startsWith('/admin'))
    ),
    { initialValue: false }
  );

  readonly showStorefront = computed(() => !this.isAdmin());
}
