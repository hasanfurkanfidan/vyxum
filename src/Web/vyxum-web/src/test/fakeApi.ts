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
    'weighing.Net': "Net",
    'weighing.Gross': "Brüt",
    'weighing.Tare': "Dara",
    'weighing.Amount': "Tutar",
    'weighing.PricePerKg': "{0} / kg",
    'weighing.SaveRecord': "Tartımı kaydet",
    'weighing.SaveAndPrint': "Kaydet ve etiket bas",
    'weighing.TakeTare': "Dara al",
    'weighing.ClearTare': "Darayı kaldır",
    'weighing.Zero': "Sıfırla",
    'weighing.ProductSearch': "Ürün, PLU ya da ad",
    'weighing.NoProduct': "Ürünsüz",
    'weighing.Lot': "Parti",
    'weighing.RecentRecords': "Son tartımlar",
    'weighing.AllRecords': "Tüm kayıtlar",
    'weighing.Saved': "kaydedildi",
    'weighing.Cancelled': "iptal",
    'weighing.SelectDevice': "Terazi seçin",
    'weighing.DeviceConnected': "terazi bağlı",
    'weighing.DeviceDisconnected': "terazi bağlı değil",
    'weighing.DeviceConnecting': "teraziye bağlanılıyor",
    'weighing.StateStable': "sabit",
    'weighing.StateMotion': "hareketli",
    'weighing.StateOverload': "aşırı yük",
    'weighing.StateUnderload': "düşük yük",
    'weighing.Saving': "Kaydediliyor…",
    'weighing.SaveFailed': "Tartım kaydedilemedi.",
    'weighing.Retrying': "Bağlantı koptu, yeniden deneniyor…",
    'weighing.ZeroDone': "Terazi sıfırlandı.",
    'weighing.ZeroNotSupported': "Bu terazi buradan sıfırlanamıyor.",
    'weighing.ZeroRejected': "Terazi sıfırlamayı reddetti. Kefeyi boşaltıp ağırlık sabitlenince deneyin.",
    'weighing.ZeroUnavailable': "Teraziye ulaşılamıyor, sıfırlanamadı.",
    'weighing.NoReading': "Teraziden henüz okuma gelmedi.",
    'weighing.NotStable': "Ağırlık sabit değil. Terazi durulunca yeniden deneyin.",
    'weighing.NetTooSmall': "Net ağırlık kayıt için çok küçük.",
    'weighing.LiveWeight': "Canlı ağırlık",
    'weighing.ProductSelection': "Ürün seçimi",
    'weighing.NoRecent': "Henüz tartım yok.",
    'shell.NoDevice': 'Bağlı terazi yok',
    'records.Title': 'Tartım kayıtları',
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
    'weighing.Net': "Net",
    'weighing.Gross': "Gross",
    'weighing.Tare': "Tare",
    'weighing.Amount': "Amount",
    'weighing.PricePerKg': "{0} / kg",
    'weighing.SaveRecord': "Save weighing",
    'weighing.SaveAndPrint': "Save and print label",
    'weighing.TakeTare': "Take tare",
    'weighing.ClearTare': "Clear tare",
    'weighing.Zero': "Zero",
    'weighing.ProductSearch': "Product, PLU or name",
    'weighing.NoProduct': "No product",
    'weighing.Lot': "Lot",
    'weighing.RecentRecords': "Recent weighings",
    'weighing.AllRecords': "All records",
    'weighing.Saved': "saved",
    'weighing.Cancelled': "cancelled",
    'weighing.SelectDevice': "Select a scale",
    'weighing.DeviceConnected': "scale connected",
    'weighing.DeviceDisconnected': "scale not connected",
    'weighing.DeviceConnecting': "connecting to the scale",
    'weighing.StateStable': "stable",
    'weighing.StateMotion': "in motion",
    'weighing.StateOverload': "overload",
    'weighing.StateUnderload': "underload",
    'weighing.Saving': "Saving…",
    'weighing.SaveFailed': "The weighing could not be saved.",
    'weighing.Retrying': "Connection lost, retrying…",
    'weighing.ZeroDone': "The scale was zeroed.",
    'weighing.ZeroNotSupported': "This scale cannot be zeroed from here.",
    'weighing.ZeroRejected': "The scale rejected zeroing. Empty the pan and wait until it is stable.",
    'weighing.ZeroUnavailable': "The scale is not reachable, so it cannot be zeroed.",
    'weighing.NoReading': "No reading from the scale yet.",
    'weighing.NotStable': "The weight is not stable. Try again when the scale settles.",
    'weighing.NetTooSmall': "The net weight is too small to record.",
    'weighing.LiveWeight': "Live weight",
    'weighing.ProductSelection': "Product selection",
    'weighing.NoRecent': "No weighings yet.",
    'shell.NoDevice': 'No scale connected',
    'records.Title': 'Weighing records',
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
