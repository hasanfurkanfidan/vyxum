// Test double for the host API: answers fetch calls from a route table.
import { vi } from 'vitest';
import type { UiModules } from '../lib/api';
import { readCultureCookie } from '../lib/culture';

export type FakeHandler = (url: URL, init: RequestInit | undefined) => Response | Promise<Response>;

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });
}

/** Sample strings per culture, as the host would merge them from the resource files. */
export const sampleStrings: Record<'tr' | 'en', Record<string, string>> = {
  tr: {
    'shell.AppName': 'vyxum',
    'shell.Core': 'Çekirdek',
    'shell.Modules': 'Modüller',
    'shell.System': 'Sistem',
    'shell.Weighing': 'Tartım',
    'shell.Records': 'Kayıtlar',
    'shell.Products': 'Ürünler',
    'shell.Language': 'Dil',
    'shell.Turkish': 'Türkçe',
    'shell.English': 'English',
    'shell.Loading': 'Yükleniyor',
    'shell.Error': 'Sunucuya ulaşılamadı.',
    'shell.Retry': 'Yeniden dene',
    'weighing.Title': 'Tartım',
    'weighing.RecordsTitle': 'Tartım kayıtları',
    'weighing.RecordsBreadcrumb': 'Kayıtlar',
    'weighing.Date': 'Tarih',
    'weighing.Time': 'Saat',
    'weighing.RecordNo': 'Kayıt no',
    'weighing.Plu': 'PLU',
    'weighing.Product': 'Ürün',
    'weighing.Lot': 'Parti',
    'weighing.Net': 'Net',
    'weighing.Amount': 'Tutar',
    'weighing.Operator': 'Operatör',
    'weighing.Status': 'Durum',
    'weighing.AllProducts': 'Tüm ürünler',
    'weighing.ExportExcel': 'Excel\'e aktar',
    'weighing.ValidSummary': '{0} geçerli kayıt, {1} iptal',
    'weighing.CancelledNotInTotals': 'iptaller toplama girmez',
    'weighing.CancelRecord': 'Kaydı iptal et',
    'weighing.CancelReason': 'İptal nedeni',
    'weighing.CancelConfirm': '{0} numaralı kayıt iptal edilsin mi? Listede kalır ama toplama girmez.',
    'weighing.CancelReasonRequired': 'İptal nedeni zorunludur.',
    'weighing.AlreadyCancelled': 'Bu kayıt zaten iptal edilmiş.',
    'weighing.Empty': 'Kayıt bulunamadı.',
    'weighing.Cancelled': 'iptal',
    'weighing.Saved': 'kaydedildi',
    'weighing.Back': 'Geri',
    'weighing.LoadMore': 'Daha fazla yükle',
    'products.Title': 'Ürün kartları',
    'products.CodeRequired': 'PLU kodu zorunludur.',
    'products.CodeTaken': 'Bu PLU kodu başka bir üründe kullanılıyor.',
    'products.NameRequired': 'Ürün adı zorunludur.',
    'products.InvalidNumber': 'Geçerli bir sayı girin, örneğin 12.50.',
    'products.NegativeNotAllowed': 'Değer negatif olamaz.',
    'products.Breadcrumb': 'Ürünler',
    'products.CountSuffix': '{0} ürün',
    'products.AddProduct': 'Ürün ekle',
    'products.Code': 'PLU',
    'products.Product': 'Ürün',
    'products.UnitPricePerKg': 'Birim fiyat (kg başına)',
    'products.PackageTare': 'Ambalaj darası (kg)',
    'products.TargetWeight': 'Hedef ağırlık (kg)',
    'products.LowerTolerance': 'Alt tolerans (kg)',
    'products.UpperTolerance': 'Üst tolerans (kg)',
    'products.ToleranceHint': 'Hedefin altında ve üstünde kabul edilen miktarlar, örneğin 0,250 için 0,005 ve 0,010',
    'products.ToleranceNeedsTarget': 'Tolerans girmeden önce hedef ağırlığı girin.',
    'products.ShelfLife': 'Raf ömrü (gün)',
    'products.Days': '{0} gün',
    'products.BarcodePrefix': 'Barkod öneki',
    'products.Ingredients': 'İçindekiler',
    'products.IngredientsHint': 'Alerjenleri yıldızla işaretle',
    'products.Name': 'Ürün adı',
    'products.Save': 'Kaydet',
    'products.Deactivate': 'Ürünü pasifleştir',
    'products.Activate': 'Ürünü etkinleştir',
    'products.ShowInactive': 'Pasif ürünleri göster',
    'products.Inactive': 'Pasif',
    'products.Saved': 'Kaydedildi',
    'products.NewProduct': 'Yeni ürün',
    'products.Empty': 'Henüz ürün yok',
    'products.SearchPlaceholder': 'PLU ya da ad',
  },
  en: {
    'shell.AppName': 'vyxum',
    'shell.Core': 'Core',
    'shell.Modules': 'Modules',
    'shell.System': 'System',
    'shell.Weighing': 'Weighing',
    'shell.Records': 'Records',
    'shell.Products': 'Products',
    'shell.Language': 'Language',
    'shell.Turkish': 'Türkçe',
    'shell.English': 'English',
    'shell.Loading': 'Loading',
    'shell.Error': 'The server could not be reached.',
    'shell.Retry': 'Try again',
    'weighing.Title': 'Weighing',
    'weighing.RecordsTitle': 'Weighing records',
    'weighing.RecordsBreadcrumb': 'Records',
    'weighing.Date': 'Date',
    'weighing.Time': 'Time',
    'weighing.RecordNo': 'Record no',
    'weighing.Plu': 'PLU',
    'weighing.Product': 'Product',
    'weighing.Lot': 'Lot',
    'weighing.Net': 'Net',
    'weighing.Amount': 'Amount',
    'weighing.Operator': 'Operator',
    'weighing.Status': 'Status',
    'weighing.AllProducts': 'All products',
    'weighing.ExportExcel': 'Export to Excel',
    'weighing.ValidSummary': '{0} valid records, {1} cancelled',
    'weighing.CancelledNotInTotals': 'cancelled records are not included in totals',
    'weighing.CancelRecord': 'Cancel record',
    'weighing.CancelReason': 'Reason for cancellation',
    'weighing.CancelConfirm': 'Cancel record {0}? It stays in the list but is not included in totals.',
    'weighing.CancelReasonRequired': 'Enter a reason for the cancellation.',
    'weighing.AlreadyCancelled': 'This record is already cancelled.',
    'weighing.Empty': 'No records found.',
    'weighing.Cancelled': 'cancelled',
    'weighing.Saved': 'saved',
    'weighing.Back': 'Back',
    'weighing.LoadMore': 'Load more',
    'products.Title': 'Product cards',
    'products.CodeRequired': 'PLU code is required.',
    'products.CodeTaken': 'This PLU code is already used by another product.',
    'products.NameRequired': 'Product name is required.',
    'products.InvalidNumber': 'Enter a valid number, for example 12.50.',
    'products.NegativeNotAllowed': 'The value cannot be negative.',
    'products.Breadcrumb': 'Products',
    'products.CountSuffix': '{0} products',
    'products.AddProduct': 'Add product',
    'products.Code': 'PLU',
    'products.Product': 'Product',
    'products.UnitPricePerKg': 'Unit price per kg',
    'products.PackageTare': 'Package tare (kg)',
    'products.TargetWeight': 'Target weight (kg)',
    'products.LowerTolerance': 'Lower tolerance (kg)',
    'products.UpperTolerance': 'Upper tolerance (kg)',
    'products.ToleranceHint': 'Amounts below and above the target that are still acceptable, for example 0.250 with 0.005 and 0.010',
    'products.ToleranceNeedsTarget': 'Enter a target weight before setting tolerances.',
    'products.ShelfLife': 'Shelf life (days)',
    'products.Days': '{0} days',
    'products.BarcodePrefix': 'Barcode prefix',
    'products.Ingredients': 'Ingredients',
    'products.IngredientsHint': 'Mark allergens with asterisks',
    'products.Name': 'Product name',
    'products.Save': 'Save',
    'products.Deactivate': 'Deactivate product',
    'products.Activate': 'Activate product',
    'products.ShowInactive': 'Show inactive products',
    'products.Inactive': 'Inactive',
    'products.Saved': 'Saved',
    'products.NewProduct': 'New product',
    'products.Empty': 'No products yet',
    'products.SearchPlaceholder': 'PLU or name',
  },
};

export interface FakeApiOptions {
  modules?: string[];
  capabilities?: string[];
  routes?: Record<string, FakeHandler>;
}

/** Replaces global fetch. `/api/ui/strings` answers in the language of the culture cookie. */
export function installFakeApi(options: FakeApiOptions = {}) {
  const modules: UiModules = {
    modules: (options.modules ?? ['weighing', 'products']).map((id) => ({ id, version: '1.0.0' })),
    capabilities: options.capabilities ?? [],
  };

  const routes: Record<string, FakeHandler> = {
    'GET /api/ui/modules': () => jsonResponse(modules),
    'GET /api/ui/strings': () => {
      const culture = readCultureCookie() ?? 'tr';
      return jsonResponse({ culture, strings: sampleStrings[culture] });
    },
    ...options.routes,
  };

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://localhost');
    const method = (init?.method ?? 'GET').toUpperCase();
    const handler = routes[`${method} ${url.pathname}`];
    if (!handler) return jsonResponse({ title: 'Not found' }, 404);
    return handler(url, init);
  });

  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Number of calls to one route, for example `callsTo(fetchMock, 'GET /api/ui/strings')`. */
export function callsTo(fetchMock: ReturnType<typeof installFakeApi>, route: string): number {
  const [method, path] = route.split(' ');
  return fetchMock.mock.calls.filter(([input, init]) => {
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    return (init?.method ?? 'GET').toUpperCase() === method && new URL(href, 'http://localhost').pathname === path;
  }).length;
}
