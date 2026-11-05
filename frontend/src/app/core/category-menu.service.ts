import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { Category } from './models';

export interface MenuBrand {
  name: string;
  slug: string;
  productCount: number;
}

interface MenuCategory {
  slug: string;
  brands: MenuBrand[];
  productCount: number;
}

/** Category tree plus the brands in each category, loaded once for the mega menu and the mobile drawer. */
@Injectable({ providedIn: 'root' })
export class CategoryMenuService {
  private readonly api = inject(ApiService);
  private requested = false;

  readonly categories = signal<Category[]>([]);
  private readonly menu = signal<MenuCategory[]>([]);

  /** Mobile bottom-sheet state; the bottom nav's "Category" item toggles it. */
  readonly drawerOpen = signal(false);

  readonly ready = computed(() => this.categories().length > 0);

  load(): void {
    if (this.requested) {
      return;
    }

    this.requested = true;
    this.api.get<Category[]>('/categories').subscribe({
      next: (tree) => this.categories.set((tree ?? []).filter((c) => c.isActive)),
      error: () => (this.requested = false)
    });
    this.api.get<MenuCategory[]>('/categories/menu').subscribe({
      next: (menu) => this.menu.set(menu ?? []),
      error: () => undefined
    });
  }

  brandsFor(slug: string): MenuBrand[] {
    return this.menu().find((m) => m.slug === slug)?.brands ?? [];
  }

  productCountFor(slug: string): number {
    return this.menu().find((m) => m.slug === slug)?.productCount ?? 0;
  }

  openDrawer(): void {
    this.load();
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  toggleDrawer(): void {
    this.drawerOpen() ? this.closeDrawer() : this.openDrawer();
  }
}
