import { clearCustomerSession, readCustomerSession, writeCustomerSession, type CustomerSession } from "./customer-auth";
import { clearAdminSession, readAdminSession, writeAdminSession, type AdminSession } from "./admin-auth";

export const api = process.env.NEXT_PUBLIC_API_URL || "/api/v1";
export const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 });
export const statusLabels: Record<string, string> = { PENDING: "Chờ xác nhận", CONFIRMED: "Đã xác nhận", PREPARING: "Đang pha chế", READY: "Sẵn sàng nhận", DELIVERING: "Đang giao", COMPLETED: "Hoàn thành", CANCELLED: "Đã hủy", REJECTED: "Quán từ chối" };
export const paymentLabels: Record<string, string> = { UNPAID: "Chưa thanh toán", PAID: "Đã thanh toán", REVIEW: "Đã nhận tiền · cần đối soát" };
export type Category = { id: string; name: string; slug: string; active?: boolean; sortOrder?: number };
export type Product = { id: string; name: string; slug: string; description: string | null; imageUrl: string | null; categoryName: string; categoryId: string; basePrice: number; salePrice: number | null; bestSeller: boolean; featured: boolean; available: boolean };
export type Option = { id: string; name: string };
export type PricedOption = Option & { price: number };
export type ProductDetail = { product: Product; sizes: PricedOption[]; toppings: PricedOption[]; sugarOptions: Option[]; iceOptions: Option[] };
export type Review = { rating: number; comment: string | null; createdAt: string };
export type Order = { id: string; orderCode: string; guestName: string; guestPhone: string; deliveryAddress: string; pickupLocation: string | null; note: string | null; subtotal: number; shippingFee: number; discount: number; total: number; voucherCode: string | null; deliveryType: string; paymentMethod: string; paymentStatus: string; orderStatus: string; version: number; allowedStatuses: string[]; canCancel: boolean; trackingToken: string; createdAt: string; items: { productId: string; name: string; quantity: number; price: number; total: number; size: string; sugar: string; ice: string; toppings: string; note: string }[] };
export type Store = { name: string; address: string; phone: string | null; shippingFee: number; freeShippingFrom: number; paymentMode: string; paymentProvider: string; onlinePaymentEnabled: boolean; mockPaymentEnabled: boolean };
export type Quote = { subtotal: number; shippingFee: number; discount: number; total: number; voucherCode: string | null };
export type PaymentIntent = { reference: string; amount: number; isDemo: boolean; status: string; provider: string; checkoutUrl: string | null; expiresAt: string; needsReview: boolean };
export class ApiError extends Error { constructor(message: string, public status: number) { super(message); } }

const refreshing: Partial<Record<"customer" | "admin", Promise<void>>> = {};
async function refresh(kind: "customer" | "admin") {
  if (refreshing[kind]) return refreshing[kind];
  refreshing[kind] = (async () => {
    const session = kind === "customer" ? readCustomerSession() : readAdminSession();
    if (!session?.refreshToken) throw new ApiError("Phiên đã hết hạn. Vui lòng đăng nhập lại.", 401);
    const result = await request<{ accessToken: string; refreshToken: string; expiresIn: number }>("/auth/refresh", { method: "POST", body: JSON.stringify({ refreshToken: session.refreshToken }) });
    const renewed = { ...session, accessToken: result.accessToken, refreshToken: result.refreshToken, expiresAt: Date.now() + result.expiresIn * 1000 };
    if (kind === "customer") writeCustomerSession(renewed as CustomerSession);
    else writeAdminSession(renewed as AdminSession);
  })().catch((error) => {
    if (error instanceof ApiError && error.status === 401) {
      if (kind === "customer") clearCustomerSession(); else clearAdminSession();
    }
    throw error;
  }).finally(() => { delete refreshing[kind]; });
  return refreshing[kind];
}

export async function request<T>(path: string, init: RequestInit = {}, auth?: "customer" | "admin", retried = false): Promise<T> {
  const session = auth === "customer" ? readCustomerSession() : auth === "admin" ? readAdminSession() : null;
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (session) headers.set("Authorization", `Bearer ${session.accessToken}`);
  let response: Response;
  try { response = await fetch(api + path, { ...init, headers }); }
  catch (error) { if (init.signal?.aborted) throw error; throw new Error("Không kết nối được với quán. Vui lòng thử lại."); }
  if (response.status === 401 && auth && !retried && session?.refreshToken) {
    const latest = auth === "customer" ? readCustomerSession() : readAdminSession();
    if (latest?.accessToken === session.accessToken) await refresh(auth);
    return request<T>(path, init, auth, true);
  }
  if (response.status === 204) return undefined as T;
  const body = await response.json().catch(() => null);
  if (!response.ok || !body?.success) throw new ApiError(body?.message || "Không thể xử lý yêu cầu. Vui lòng thử lại.", response.status);
  return body.data as T;
}

export function orderItems(items: import("./cart").CartItem[]) {
  return items.map(item => ({ productId: item.productId, sizeId: item.selection?.size.id, sugarOptionId: item.selection?.sugar.id, iceOptionId: item.selection?.ice.id, toppingIds: item.selection?.toppings.map(t => t.id) || [], quantity: item.quantity, note: item.selection?.note }));
}
