"use client";
import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ArrowRight, BarChart3, Clock, Coffee, FlaskConical, LogOut, Mail, Package, RefreshCw, Search, Settings, TrendingUp } from "lucide-react";
import { clearAdminSession, readAdminSession, type AdminSession } from "../../lib/admin-auth";
import { ApiError, money, paymentLabels, request, statusLabels, type Order, type Store } from "../../lib/api";

type Report = { totalOrders: number; completedOrders: number; revenue: number; averageOrderValue: number; byStatus: Record<string, number>; daily: { date: string; revenue: number; orders: number }[] };
type Integrations = { gmailConfigured: boolean; paymentProvider: string; paymentMode: string; ipnUrl: string; returnUrl: string };
type PaymentReview = { reference: string; provider: string; amount: number; gatewayTransactionId: string; orderCode: string; orderStatus: string; paymentStatus: string };
const date = new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "short" });
export default function AdminPage() {
  const router = useRouter();
  const [session, setSession] = useState<AdminSession | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [report, setReport] = useState<Report | null>(null);
  const [store, setStore] = useState<Store | null>(null);
  const [integrations, setIntegrations] = useState<Integrations | null>(null);
  const [reviews, setReviews] = useState<PaymentReview[]>([]);
  const [mailBusy, setMailBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [filter, setFilter] = useState("ALL");
  const [query, setQuery] = useState("");
  const [pending, setPending] = useState("");
  const endSession = useCallback(() => { clearAdminSession(); router.replace("/admin/login"); }, [router]);
  const handleError = useCallback((error: unknown) => { if (error instanceof ApiError && [401, 403].includes(error.status)) endSession(); else setError(error instanceof Error ? error.message : "Không tải được dữ liệu."); }, [endSession]);
  const load = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    try { const [orders, report, store, integrations, reviews] = await Promise.all([request<Order[]>("/admin/orders", {}, "admin"), request<Report>("/admin/reports/summary", {}, "admin"), request<Store>("/store"), request<Integrations>("/admin/integrations", {}, "admin"), request<PaymentReview[]>("/admin/payments/review", {}, "admin")]); setOrders(orders); setReport(report); setStore(store); setIntegrations(integrations); setReviews(reviews); }
    catch (error) { handleError(error); }
    finally { setLoading(false); }
  }, [handleError]);
  useEffect(() => {
    const active = readAdminSession(); if (!active) { router.replace("/admin/login"); return; }
    const timer = setTimeout(() => { setSession(active); void load(); }, 0);
    const poll = setInterval(() => { if (document.visibilityState === "visible") void load(true); }, 15000);
    return () => { clearTimeout(timer); clearInterval(poll); };
  }, [load, router]);
  async function change(event: FormEvent<HTMLFormElement>, order: Order) {
    event.preventDefault(); const status = String(new FormData(event.currentTarget).get("status") || ""); if (!status || pending) return;
    setPending(order.orderCode); setError(""); setNotice("");
    try { await request(`/admin/orders/${order.orderCode}/status`, { method: "PATCH", body: JSON.stringify({ status, expectedVersion: order.version }) }, "admin"); setNotice(`Đơn ${order.orderCode}: ${statusLabels[status]}.`); await load(true); }
    catch (error) { handleError(error); await load(true); }
    finally { setPending(""); }
  }
  async function mock(order: Order) {
    if (pending) return; setPending(order.orderCode); setError(""); setNotice("");
    try {
      await request("/payments/intents", { method: "POST", headers: { "Idempotency-Key": `adminpay_${order.id}_${order.version}` }, body: JSON.stringify({ orderCode: order.orderCode, phone: order.guestPhone }) });
      await request(`/admin/payments/${order.orderCode}/mock`, { method: "POST" }, "admin"); setNotice(`Đã mô phỏng thanh toán cho ${order.orderCode}. Không có giao dịch tiền thật.`); await load(true);
    } catch (error) { handleError(error); }
    finally { setPending(""); }
  }
  async function logout() {
    try { const active = readAdminSession(); if (active) await request("/auth/logout", { method: "POST", body: JSON.stringify({ refreshToken: active.refreshToken }) }); endSession(); }
    catch (error) { handleError(error); }
  }
  async function testMail() {
    setMailBusy(true); setError(""); setNotice("");
    try { await request("/admin/integrations/gmail/test", { method: "POST" }, "admin"); setNotice("Đã gửi thư thử đến chính Gmail được cấu hình. Hãy kiểm tra hộp thư và thư mục Spam."); }
    catch (error) { handleError(error); }
    finally { setMailBusy(false); }
  }
  const shown = orders.filter(o => (filter === "ALL" || o.orderStatus === filter) && (!query.trim() || `${o.orderCode} ${o.guestName} ${o.guestPhone}`.toLowerCase().includes(query.trim().toLowerCase())));
  const activeCount = Object.entries(report?.byStatus || {}).filter(([status]) => !["COMPLETED", "CANCELLED", "REJECTED"].includes(status)).reduce((sum, [, count]) => sum + count, 0);
  const maxRevenue = Math.max(1, ...(report?.daily.map(d => d.revenue) || []));
  return <main className="admin-page"><header className="admin-header"><div><Link href="/" className="admin-brand"><span className="brand-mark"><Coffee size={23} /></span><span>Quê Drinks<small>QUẢN LÝ CỬA HÀNG</small></span></Link><nav className="admin-nav"><Link href="/admin/manage"><Settings size={17} /> Sản phẩm & ưu đãi</Link><button type="button" onClick={() => void logout()}><LogOut size={17} /><span>Đăng xuất</span></button></nav></div></header><div className="admin-container"><section className="dashboard-welcome"><div><p className="eyebrow">MỘT NGÀY KINH DOANH THẬT TỐT</p><h1>Xin chào, {session?.displayName || "quản trị viên"}.</h1><p>Nhận đơn, pha chế và theo dõi hiệu quả cửa hàng tại một nơi.</p></div><button type="button" className="button button--light" disabled={loading} onClick={() => { setError(""); void load(); }}><RefreshCw size={17} /> Tải mới</button></section>
    {error && <p className="alert alert--error" role="alert">{error}</p>}{notice && <p className="alert alert--success" role="status">{notice}</p>}
    <section className="panel"><div className="panel-heading"><h2>Kết nối dịch vụ</h2><span>{integrations?.paymentMode || "Đang kiểm tra…"}</span></div><p>Gmail: <b>{integrations?.gmailConfigured ? "Đã cấu hình" : "Chưa cấu hình"}</b> · Thanh toán: <b>{integrations?.paymentProvider || "—"}</b></p><button type="button" className="button button--secondary" disabled={!integrations?.gmailConfigured || mailBusy} onClick={() => void testMail()}><Mail size={17} />{mailBusy ? "Đang gửi…" : "Gửi email thử đến Gmail của quán"}</button>{integrations?.paymentProvider === "VNPAY" && <details><summary>Địa chỉ đăng ký với VNPay</summary><p className="muted">IPN: {integrations.ipnUrl}</p><p className="muted">Return: {integrations.returnUrl}</p></details>}</section>
    {reviews.length > 0 && <section className="panel"><div className="panel-heading"><h2>Giao dịch cần đối soát</h2><span>{reviews.length} giao dịch tải gần nhất</span></div><p className="alert alert--error">Đã nhận xác nhận tiền cho đơn đã hủy hoặc đơn đã có thanh toán khác. Kiểm tra trên cổng merchant và hoàn tiền thủ công khi cần; hệ thống giữ nguyên trạng thái xử lý đơn.</p>{reviews.map(payment => <article className="admin-order" key={payment.reference}><h3>{payment.orderCode} · {money.format(payment.amount)}</h3><p>{payment.provider} · {payment.reference}</p><p>Mã giao dịch tại cổng: {payment.gatewayTransactionId} · {statusLabels[payment.orderStatus]}</p></article>)}</section>}
    <section className="dashboard-stats" aria-label="Tổng quan"><div><span><Package size={20} /> Tổng đơn</span><strong>{report?.totalOrders ?? "—"}</strong><small>Toàn bộ lịch sử</small></div><div><span><Clock size={20} /> Đang xử lý</span><strong>{report ? activeCount : "—"}</strong><small>{report?.byStatus.PENDING || 0} đơn chờ tiếp nhận</small></div><div><span><TrendingUp size={20} /> Doanh thu</span><strong>{report ? money.format(report.revenue) : "—"}</strong><small>Đơn hoàn thành và đã thanh toán</small></div><div><span><BarChart3 size={20} /> Trung bình đơn</span><strong>{report ? money.format(report.averageOrderValue) : "—"}</strong><small>{report?.completedOrders || 0} đơn hoàn thành</small></div></section>
    <section className="panel report-panel"><div className="panel-heading"><h2>Doanh thu theo ngày đặt · 7 ngày gần đây</h2><span>Giờ Việt Nam</span></div>{!report?.daily.length ? <p className="muted">Chưa có đơn hoàn thành và đã thanh toán trong khoảng này.</p> : <div className="revenue-bars">{report.daily.map(d => <div key={d.date}><span>{d.date.slice(5).split("-").reverse().join("/")}</span><div className="revenue-bar-track"><i style={{ width: `${Math.max(1, d.revenue / maxRevenue * 100)}%` }} /></div><strong>{money.format(d.revenue)}</strong><small>{d.orders} đơn</small></div>)}</div>}</section>
    <section className="panel admin-orders-panel"><div className="panel-heading"><div><p className="eyebrow text-emerald-800">BẢNG ĐIỀU HÀNH</p><h2>Đơn hàng cần chăm chút</h2></div><span>{shown.length} / {orders.length} đơn tải gần nhất</span></div><label className="search-field"><Search size={18} /><span className="sr-only">Tìm đơn hàng</span><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Mã đơn, tên khách hoặc số điện thoại…" /></label><div className="filter-scroll admin-filters">{["ALL", ...Object.keys(statusLabels)].map(status => <button key={status} className={filter === status ? "is-active" : ""} aria-pressed={filter === status} onClick={() => setFilter(status)}>{status === "ALL" ? "Tất cả" : statusLabels[status]} <small>{status === "ALL" ? report?.totalOrders || 0 : report?.byStatus[status] || 0}</small></button>)}</div>
    {loading ? <p className="loading-message">Đang tải đơn hàng…</p> : !shown.length ? <div className="small-empty"><Package size={30} /><p>Chưa có đơn phù hợp với bộ lọc này.</p></div> : <div className="admin-order-list">{shown.map(order => <article className="admin-order" key={order.id}><div className="admin-order__top"><div><h3>{order.orderCode}</h3><small>{date.format(new Date(order.createdAt))} · {order.deliveryType === "PICKUP" ? "Nhận tại quán" : "Giao tận nơi"}</small></div><span className={`order-status order-status--${order.orderStatus.toLowerCase()}`}>{statusLabels[order.orderStatus]}</span></div><div className="admin-order__info"><div><b>{order.guestName}</b><p>{order.guestPhone}</p><p className="muted">{order.items.map(i => `${i.quantity}× ${i.name}`).join(", ")}</p></div><div className="admin-order__total"><strong>{money.format(order.total)}</strong><span>{paymentLabels[order.paymentStatus] || order.paymentStatus}</span><small>{order.paymentMethod === "COD" ? "Trả khi nhận" : "Thanh toán trực tuyến"}</small></div></div><details className="admin-order-details"><summary>Chi tiết giao nhận & tùy chọn</summary><p><b>Địa chỉ:</b> {order.deliveryAddress}</p>{order.note && <p><b>Lời nhắn:</b> {order.note}</p>}{order.items.map((i, index) => <p key={index}><b>{i.quantity}× {i.name}</b> · {[i.size, i.sugar, i.ice, i.toppings, i.note].filter(Boolean).join(" · ")} · {money.format(i.total)}</p>)}<p>Tiền món {money.format(order.subtotal)} + giao nhận {money.format(order.shippingFee)} − ưu đãi {money.format(order.discount)} {order.voucherCode && `(${order.voucherCode})`}</p></details><div className="admin-order__actions">{order.allowedStatuses.length > 0 ? <form onSubmit={e => void change(e, order)} key={order.version}><label className="sr-only" htmlFor={`status-${order.id}`}>Trạng thái mới cho {order.orderCode}</label><select id={`status-${order.id}`} name="status" required defaultValue="" disabled={!!pending}><option value="" disabled>Chọn bước xử lý tiếp theo</option>{order.allowedStatuses.map(status => <option key={status} value={status}>{statusLabels[status]}</option>)}</select><button className="button button--primary" disabled={!!pending}>{pending === order.orderCode ? "Đang xử lý…" : "Cập nhật"}<ArrowRight size={16} /></button></form> : <small>Đơn đã kết thúc; giữ nguyên lịch sử.</small>}{store?.mockPaymentEnabled && order.paymentMethod === "BANK_TRANSFER" && order.paymentStatus === "UNPAID" && order.orderStatus === "PENDING" && <button type="button" className="button button--secondary" disabled={!!pending} onClick={() => void mock(order)}><FlaskConical size={16} /> Mô phỏng đã trả tiền</button>}</div></article>)}</div>}
    <p className="panel-footnote">Tự làm mới mỗi 15 giây khi mở trang. Danh sách tối đa 500 đơn gần nhất; báo cáo tính toàn bộ đơn trong cơ sở dữ liệu.</p></section><p className="info-note">{store?.paymentMode === "LIVE" ? "Chỉ ghi nhận đã trả tiền khi nhận IPN hợp lệ từ VNPay. Giao dịch cần đối soát hiển thị riêng phía trên." : store?.paymentMode === "SANDBOX" ? "VNPay sandbox dùng thẻ thử nghiệm; số liệu ở đây phục vụ kiểm thử." : store?.paymentProvider === "MOCK" ? "Chế độ MOCK: quản trị mô phỏng kết quả; không có giao dịch tiền thật." : "Thanh toán trực tuyến chưa được bật. Khách có thể trả khi nhận hàng."}</p>
  </div></main>;
}
