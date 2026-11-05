import { CanActivateFn, Routes } from '@angular/router';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './core/auth.service';

// `state.url` is the page being opened; `router.url` is still the page being left, which sent
// shoppers back to the home page after signing in.
const areaGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAdmin()) {
    return true;
  }

  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

const customerGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isSignedIn()) {
    return true;
  }

  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent)
  },
  {
    path: 'shop',
    loadComponent: () => import('./features/catalog/catalog.component').then((m) => m.CatalogComponent)
  },
  {
    path: 'search',
    data: { noindex: true },
    loadComponent: () => import('./features/catalog/catalog.component').then((m) => m.CatalogComponent)
  },
  {
    path: 'offer',
    loadComponent: () => import('./features/offers/offers.component').then((m) => m.OffersComponent)
  },
  {
    path: 'offers',
    redirectTo: 'offer',
    pathMatch: 'full'
  },
  {
    path: 'offer/:slug',
    loadComponent: () => import('./features/offers/offer-detail.component').then((m) => m.OfferDetailComponent)
  },
  {
    path: 'pre-order',
    loadComponent: () => import('./features/pre-order/pre-order.component').then((m) => m.PreOrderComponent)
  },
  {
    path: 'category/:slug',
    loadComponent: () => import('./features/catalog/catalog.component').then((m) => m.CatalogComponent)
  },
  {
    path: 'categories/:slug',
    loadComponent: () => import('./features/catalog/catalog.component').then((m) => m.CatalogComponent)
  },
  {
    path: 'brands',
    loadComponent: () => import('./features/brands/brands.component').then((m) => m.BrandsComponent)
  },
  {
    path: 'brands/:slug',
    loadComponent: () => import('./features/catalog/catalog.component').then((m) => m.CatalogComponent)
  },
  {
    path: 'compare',
    data: { noindex: true },
    loadComponent: () => import('./features/compare/compare.component').then((m) => m.CompareComponent)
  },
  {
    path: 'product-compare/:slugs',
    data: { noindex: true },
    loadComponent: () => import('./features/compare/compare.component').then((m) => m.CompareComponent)
  },
  {
    path: 'wishlist',
    data: { noindex: true },
    canActivate: [customerGuard],
    loadComponent: () => import('./features/wishlist/wishlist.component').then((m) => m.WishlistComponent)
  },
  {
    path: 'forgot-password',
    data: { noindex: true },
    loadComponent: () => import('./features/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent)
  },
  {
    path: 'account/profile',
    canActivate: [customerGuard],
    data: { noindex: true },
    loadComponent: () => import('./features/account/account-settings.component').then((m) => m.AccountSettingsComponent)
  },
  {
    path: 'support',
    data: { form: 'support' },
    loadComponent: () => import('./features/forms/contact-form.component').then((m) => m.ContactFormComponent)
  },
  {
    path: 'trade-in',
    data: { form: 'trade-in' },
    loadComponent: () => import('./features/forms/contact-form.component').then((m) => m.ContactFormComponent)
  },
  {
    path: 'corporate',
    data: { form: 'corporate' },
    loadComponent: () => import('./features/forms/contact-form.component').then((m) => m.ContactFormComponent)
  },
  {
    path: 'feedback',
    data: { form: 'feedback' },
    loadComponent: () => import('./features/forms/contact-form.component').then((m) => m.ContactFormComponent)
  },
  {
    path: 'about',
    data: { slug: 'about' },
    loadComponent: () => import('./features/policy/policy.component').then((m) => m.PolicyComponent)
  },
  {
    path: 'faq',
    data: { slug: 'faq' },
    loadComponent: () => import('./features/policy/policy.component').then((m) => m.PolicyComponent)
  },
  {
    path: 'career',
    data: { slug: 'career' },
    loadComponent: () => import('./features/policy/policy.component').then((m) => m.PolicyComponent)
  },
  {
    path: 'blogs',
    loadComponent: () => import('./features/blog/blog.component').then((m) => m.BlogListComponent)
  },
  {
    path: 'blogs/:slug',
    loadComponent: () => import('./features/blog/blog.component').then((m) => m.BlogPostComponent)
  },
  {
    path: 'shop-location',
    loadComponent: () => import('./features/store/store-location.component').then((m) => m.StoreLocationComponent)
  },
  {
    path: 'policy/:slug',
    loadComponent: () => import('./features/policy/policy.component').then((m) => m.PolicyComponent)
  },
  {
    path: 'product/:slug',
    loadComponent: () => import('./features/product/product.component').then((m) => m.ProductComponent)
  },
  {
    path: 'cart',
    data: { noindex: true },
    loadComponent: () => import('./features/cart/cart.component').then((m) => m.CartComponent)
  },
  {
    path: 'checkout',
    data: { noindex: true },
    loadComponent: () => import('./features/checkout/checkout.component').then((m) => m.CheckoutComponent)
  },
  {
    path: 'track',
    data: { noindex: true },
    loadComponent: () => import('./features/tracking/tracking.component').then((m) => m.TrackingComponent)
  },
  {
    path: 'track/:orderNo',
    data: { noindex: true },
    loadComponent: () => import('./features/tracking/tracking.component').then((m) => m.TrackingComponent)
  },
  {
    path: 'login',
    data: { noindex: true },
    loadComponent: () => import('./features/auth/auth.component').then((m) => m.AuthComponent)
  },
  {
    path: 'account/orders',
    data: { noindex: true },
    canActivate: [customerGuard],
    loadComponent: () =>
      import('./features/account/orders.component').then((m) => m.AccountOrdersComponent)
  },
  {
    path: 'admin',
    canActivate: [areaGuard],
    loadComponent: () => import('./admin/admin-shell.component').then((m) => m.AdminShellComponent),
    children: [
      {
        path: '',
        loadComponent: () => import('./admin/dashboard.component').then((m) => m.AdminDashboardComponent)
      },
      {
        path: 'orders',
        loadComponent: () => import('./admin/admin-orders.component').then((m) => m.AdminOrdersComponent)
      },
      {
        path: 'products',
        loadComponent: () => import('./admin/admin-products.component').then((m) => m.AdminProductsComponent)
      },
      {
        path: 'campaigns',
        loadComponent: () => import('./admin/admin-campaigns.component').then((m) => m.AdminCampaignsComponent)
      },
      {
        path: 'pre-orders',
        loadComponent: () => import('./admin/admin-pre-orders.component').then((m) => m.AdminPreOrdersComponent)
      },
      {
        path: 'blogs',
        loadComponent: () => import('./admin/admin-blogs.component').then((m) => m.AdminBlogsComponent)
      },
      {
        path: 'banners',
        loadComponent: () => import('./admin/admin-banners.component').then((m) => m.AdminBannersComponent)
      },
      {
        path: 'coupons',
        loadComponent: () => import('./admin/admin-pricing.component').then((m) => m.AdminCouponsComponent)
      },
      {
        path: 'emi',
        loadComponent: () => import('./admin/admin-pricing.component').then((m) => m.AdminEmiComponent)
      },
      {
        path: 'flash-sales',
        loadComponent: () => import('./admin/admin-pricing.component').then((m) => m.AdminFlashSalesComponent)
      },
      {
        path: 'inventory',
        loadComponent: () => import('./admin/admin-operations.component').then((m) => m.AdminInventoryComponent)
      },
      {
        path: 'reviews',
        loadComponent: () => import('./admin/admin-operations.component').then((m) => m.AdminReviewsComponent)
      },
      {
        path: 'settings',
        loadComponent: () => import('./admin/admin-operations.component').then((m) => m.AdminSettingsComponent)
      },
      {
        path: 'products/new',
        loadComponent: () => import('./admin/admin-product-editor.component').then((m) => m.AdminProductEditorComponent)
      },
      {
        path: 'products/:id',
        loadComponent: () => import('./admin/admin-product-editor.component').then((m) => m.AdminProductEditorComponent)
      },
      {
        path: 'categories',
        loadComponent: () => import('./admin/admin-taxonomy.component').then((m) => m.AdminCategoriesComponent)
      },
      {
        path: 'brands',
        loadComponent: () => import('./admin/admin-taxonomy.component').then((m) => m.AdminBrandsComponent)
      },
      {
        path: 'inbox',
        loadComponent: () => import('./admin/admin-inbox.component').then((m) => m.AdminInboxComponent)
      },
      {
        path: 'news',
        loadComponent: () => import('./admin/admin-news.component').then((m) => m.AdminNewsComponent)
      },
      {
        path: 'showcases',
        loadComponent: () => import('./admin/admin-showcases.component').then((m) => m.AdminShowcasesComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
