// Client for the Products module API (/api/products). Decimals travel as invariant strings.
import { apiGet, apiPost, apiPut, withQuery, type RequestOptions } from './api';

export interface Product {
  id: string;
  code: string;
  name: string;
  unitPrice: string | null;
  packageTare: string | null;
  shelfLifeDays: number | null;
  barcodePrefix: string | null;
  ingredients: string | null;
  isActive: boolean;
  updatedAt: string;
}

export interface ProductInput {
  code: string;
  name: string;
  unitPrice: string | null;
  packageTare: string | null;
  shelfLifeDays: number | null;
  barcodePrefix: string | null;
  ingredients: string | null;
}

/** Fields of the product card that the API validates. */
export const productFields = ['code', 'name', 'unitPrice', 'packageTare', 'shelfLifeDays', 'barcodePrefix', 'ingredients'] as const;
export type ProductField = (typeof productFields)[number];

export function listProducts(search: string, includeInactive: boolean, options?: RequestOptions): Promise<Product[]> {
  const path = withQuery('/api/products', { search: search.trim(), includeInactive: includeInactive ? true : undefined });
  return apiGet<Product[]>(path, options);
}

export function createProduct(input: ProductInput): Promise<Product> {
  return apiPost<Product>('/api/products', input);
}

export function updateProduct(id: string, input: ProductInput): Promise<Product> {
  return apiPut<Product>(`/api/products/${id}`, input);
}

export function setProductActive(id: string, active: boolean): Promise<Product> {
  return apiPost<Product>(`/api/products/${id}/${active ? 'activate' : 'deactivate'}`);
}
