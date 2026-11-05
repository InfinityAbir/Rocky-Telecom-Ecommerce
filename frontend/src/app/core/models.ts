export interface ApiEnvelope<T> {
  hasError: boolean;
  message: string | null;
  content: T;
  errors?: Record<string, string[]>;
}

export interface Category {
  id: number;
  parentId: number | null;
  name: string;
  slug: string;
  icon: string | null;
  sortOrder: number;
  isActive: boolean;
  seoTitle?: string | null;
  seoDesc?: string | null;
  children: Category[];
}

export interface Brand {
  id: number;
  name: string;
  slug: string;
  logoUrl: string | null;
  sortOrder: number;
  isActive: boolean;
}

export interface ProductCard {
  id: number;
  slug: string;
  name: string;
  brandName: string | null;
  brandSlug: string | null;
  categorySlug: string | null;
  badge: string | null;
  type: 'official' | 'unofficial' | 'used';
  condition: string;
  thumbUrl: string | null;
  regularPrice: number;
  salePrice: number | null;
  effectivePrice: number;
  discountPercent: number;
  inStock: boolean;
  stockTotal: number;
  isFeatured: boolean;
  isPreOrder: boolean;
  averageRating: number | null;
  reviewCount: number;
}

export interface Variant {
  id: number;
  sku: string;
  color: string | null;
  colorHex: string | null;
  ram: string | null;
  storage: string | null;
  region: string | null;
  simType: string | null;
  regularPrice: number;
  salePrice: number | null;
  effectivePrice: number;
  discountPercent: number;
  stock: number;
  inStock: boolean;
  imageIds: number[];
}

export interface SpecGroup {
  name: string;
  sortOrder: number;
  fields: { label: string; value: string | null; unit: string | null; isComparable: boolean }[];
}

export interface EmiPlan {
  id: number;
  bankName: string;
  months: number;
  interestPct: number;
  minPrice: number;
  monthlyAmount: number | null;
}

export interface ProductDetail {
  id: number;
  name: string;
  slug: string;
  shortDesc: string | null;
  description: string | null;
  brand: Brand | null;
  category: { id: number; name: string; slug: string } | null;
  type: 'official' | 'unofficial' | 'used';
  condition: string;
  warrantyType: string;
  warrantyMonths: number | null;
  badge: string | null;
  isFeatured: boolean;
  isPreOrder: boolean;
  isActive: boolean;
  seoTitle: string | null;
  seoDesc: string | null;
  images: { id: number; url: string; alt: string | null; sortOrder: number; variantId: number | null }[];
  variants: Variant[];
  specGroups: SpecGroup[];
  emiPlans: EmiPlan[];
  deliveryZones: { id: number; name: string; charge: number; estimatedDays: number }[];
  rating: { average: number | null; count: number; histogram: Record<string, number> };
  relatedProducts: ProductCard[];
}

export interface ProductDetailResult {
  product: ProductDetail | null;
  redirectSlug: string | null;
}

export interface ProductListResult {
  page: { items: ProductCard[]; total: number; page: number; pageSize: number; totalPages: number };
  filters: {
    brands: Brand[];
    ram: string[];
    storage: string[];
    color: string[];
    minPrice: number;
    maxPrice: number;
    colorSwatches?: Record<string, string> | null;
  };
}

export interface HomePayload {
  hero: { id: number; placement: string; title: string | null; imageUrl: string; link: string | null; sortOrder: number }[];
  promo: { id: number; placement: string; title: string | null; imageUrl: string; link: string | null; sortOrder: number }[];
  categories: Category[];
  brands: Brand[];
  featured: ProductCard[];
  newArrivals: ProductCard[];
  bestDeals: ProductCard[];
  flashSale: {
    id: number;
    title: string;
    startAt: string;
    endAt: string;
    items: { variantId: number; productId: number; slug: string; name: string; thumbUrl: string | null; regularPrice: number; salePrice: number; discountPercent: number; inStock: boolean }[];
  } | null;
  store: { name: string; address: string; phone: string; email: string | null; openingHours: string | null } | null;
  news: NewsItem[];
  showcases: Showcase[];
  blogs: BlogSummary[];
}

export interface NewsItem {
  id: number;
  title: string;
  linkUrl: string | null;
  openInNewTab: boolean;
}

export interface Showcase {
  id: number;
  slug: string;
  title: string;
  subtitle: string | null;
  items: ProductCard[];
}

export interface CompareResult {
  isValid: boolean;
  reason: string | null;
  products: { id: number; name: string; slug: string; thumbUrl: string | null; effectivePrice: number; inStock: boolean }[];
  rows: { label: string; values: (string | null)[]; isDifferent: boolean }[];
}

export interface CartItem {
  id: number;
  variantId: number;
  productId: number;
  productSlug: string;
  name: string;
  variantLabel: string | null;
  thumbUrl: string | null;
  unitPrice: number;
  regularPrice: number;
  discountPercent: number;
  qty: number;
  stock: number;
  inStock: boolean;
  isAvailable: boolean;
  lineTotal: number;
}

export interface Cart {
  items: CartItem[];
  subTotal: number;
  totalQty: number;
  hasUnavailableItems: boolean;
}

export interface Quote {
  items: {
    variantId: number;
    productId: number;
    productSlug: string;
    name: string;
    variantLabel: string | null;
    thumbUrl: string | null;
    unitPrice: number;
    regularPrice: number;
    discountPercent: number;
    qty: number;
    stock: number;
    isAvailable: boolean;
    lineTotal: number;
  }[];
  subTotal: number;
  coupon: { code: string; discount: number; applied: boolean; message: string | null } | null;
  delivery: { type: string; zone: string | null; fee: number; estimatedDays: number | null };
  total: number;
  totalQty: number;
  hasUnavailableItems: boolean;
}

export interface OrderSummary {
  id: number;
  orderNo: string;
  status: string;
  paymentMethod: string;
  paymentStatus: string;
  deliveryType: string;
  total: number;
  totalQty: number;
  createdAt: string;
}

export interface OrderDetail extends OrderSummary {
  subTotal: number;
  discount: number;
  deliveryFee: number;
  couponCode: string | null;
  guestPhone: string | null;
  note: string | null;
  address: { name: string; phone: string; division: string; district: string; thana: string; line: string } | null;
  items: { id: number; variantId: number; name: string; unitPrice: number; qty: number; lineTotal: number }[];
  timeline: { status: string; at: string; changedBy: string | null; note: string | null }[];
}

export interface Tracking {
  orderNo: string;
  status: string;
  courier: string | null;
  consignmentId: string | null;
  trackingCode: string | null;
  placedAt: string;
  timeline: { status: string; at: string; changedBy: string | null; note: string | null }[];
}

export interface AuthUser {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  phoneVerified: boolean;
  roles: string[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
}

export interface AdminOrderRow {
  id: number;
  orderNo: string;
  customer: string;
  phone: string;
  status: string;
  paymentMethod: string;
  paymentStatus: string;
  deliveryType: string;
  total: number;
  totalQty: number;
  createdAt: string;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

export interface Dashboard {
  salesToday: number;
  salesWeek: number;
  salesMonth: number;
  ordersToday: number;
  ordersWeek: number;
  ordersMonth: number;
  pendingOrders: number;
  ordersByStatus: { status: string; count: number }[];
  lowStock: { variantId: number; productName: string; sku: string; variantLabel: string; stock: number }[];
  topProducts: { name: string; quantity: number; revenue: number }[];
  salesSeries: { date: string; sales: number; orders: number }[];
  paymentMix: { method: string; count: number; amount: number }[];
  recentOrders: { id: number; orderNo: string; status: string; paymentStatus: string; total: number; createdAt: string }[];
}

export interface OfferSummary {
  id: number;
  slug: string;
  title: string;
  subtitle: string | null;
  bannerUrl: string | null;
  startAt: string | null;
  endAt: string | null;
  productCount: number;
}

export interface OfferDetail {
  id: number;
  slug: string;
  title: string;
  subtitle: string | null;
  description: string | null;
  bannerUrl: string | null;
  startAt: string | null;
  endAt: string | null;
  items: ProductCard[];
}

export interface PreOrderReceipt {
  id: number;
  productName: string;
}

export interface AdminCampaign {
  id: number;
  slug: string;
  title: string;
  subtitle: string | null;
  description: string | null;
  bannerUrl: string | null;
  startAt: string | null;
  endAt: string | null;
  sortOrder: number;
  isActive: boolean;
  products: ShowcaseProduct[];
}

export interface AdminPreOrder {
  id: number;
  productId: number | null;
  productName: string | null;
  productSlug: string | null;
  name: string;
  phone: string;
  note: string | null;
  status: 'Pending' | 'Contacted' | 'Converted' | 'Cancelled';
  createdAt: string;
}

export interface AdminNewsItem {
  id: number;
  title: string;
  linkUrl: string | null;
  openInNewTab: boolean;
  sortOrder: number;
  startAt: string | null;
  endAt: string | null;
  isActive: boolean;
}

export interface ShowcaseProduct {
  id: number;
  name: string;
  slug: string;
  isActive: boolean;
}

export interface AdminShowcase {
  id: number;
  slug: string;
  title: string;
  subtitle: string | null;
  sortOrder: number;
  isActive: boolean;
  productIds: number[];
  products: ShowcaseProduct[];
}

export interface AdminProductRow {
  id: number;
  name: string;
  slug: string;
  brandName: string;
  categoryName: string;
  minPrice: number;
  maxPrice: number;
  totalStock: number;
  variantCount: number;
  isPublished: boolean;
  isFeatured: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface Profile {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  phoneVerified: boolean;
  roles: string[];
}

export interface SavedAddress {
  id: number;
  name: string;
  phone: string;
  division: string;
  district: string;
  thana: string;
  line: string;
  isDefault: boolean;
}

export interface InboxItem {
  id: number;
  kind: string;
  name: string;
  phone: string | null;
  email: string | null;
  subject: string | null;
  message: string | null;
  status: string;
  createdAt: string;
}

export interface InboxPage {
  kind: string;
  statuses: string[];
  items: InboxItem[];
  total: number;
  counts: Record<string, number>;
}

export interface BlogSummary {
  id: number;
  slug: string;
  title: string;
  excerpt: string | null;
  cover: string | null;
  publishedAt: string | null;
}

export interface BlogDetail extends BlogSummary {
  body: string;
  seoTitle: string | null;
  seoDesc: string | null;
}

export interface StoreInfo {
  name: string;
  address: string;
  phone: string;
  email: string | null;
  openingHours: string | null;
  mapLat: number | null;
  mapLng: number | null;
}

export interface Review {
  id: number;
  rating: number;
  body: string | null;
  createdAt: string;
}

export interface ReviewEligibility {
  canReview: boolean;
  reason: string | null;
}

export interface AdminBlog {
  id: number;
  slug: string;
  title: string;
  excerpt: string | null;
  cover: string | null;
  body: string;
  isPublished: boolean;
  publishedAt: string | null;
  seoTitle: string | null;
  seoDesc: string | null;
}

export interface AdminBanner {
  id: number;
  placement: string;
  title: string | null;
  imageUrl: string;
  link: string | null;
  sortOrder: number;
  startAt: string | null;
  endAt: string | null;
  isActive: boolean;
}

export interface AdminCoupon {
  id: number;
  code: string;
  type: number;
  value: number;
  minCart: number | null;
  maxDiscount: number | null;
  usageLimit: number | null;
  perUserLimit: number | null;
  startAt: string;
  endAt: string;
  usedCount: number;
  isActive: boolean;
}

export interface AdminEmiPlan {
  id: number;
  bankName: string;
  months: number;
  interestPct: number;
  minPrice: number;
  isActive: boolean;
}

export interface AdminFlashItem {
  variantId: number;
  label: string;
  regularPrice: number;
  salePrice: number;
}

export interface AdminFlashSale {
  id: number;
  title: string;
  startAt: string;
  endAt: string;
  isActive: boolean;
  items: AdminFlashItem[];
}

export interface VariantOption {
  variantId: number;
  label: string;
  regularPrice: number;
  effectivePrice: number;
  stock: number;
}

export interface InventoryRow {
  variantId: number;
  productName: string;
  productSlug: string;
  sku: string;
  label: string | null;
  stock: number;
  isActive: boolean;
}

export interface StockLogEntry {
  id: number;
  variantId: number;
  productName: string;
  label: string | null;
  delta: number;
  stockAfter: number;
  reason: string;
  at: string;
}

export interface AdminReview {
  id: number;
  productId: number;
  productName: string;
  productSlug: string;
  rating: number;
  body: string | null;
  isApproved: boolean;
  createdAt: string;
}
