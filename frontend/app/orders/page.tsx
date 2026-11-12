"use client";
import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ArrowRight, Check, CheckCircle2, Clock, CreditCard, Flame, FlaskConical, MapPin, PackageCheck, RefreshCw, Search, Truck, XCircle } from "lucide-react";
import { SiteHeader } from "../components/SiteHeader";
import { Modal } from "../components/Modal";
import { api, money, paymentLabels, request, statusLabels, type Order, type PaymentIntent, type Store } from "../../lib/api";

const steps = [{ key: "PENDING", label: "Tiếp nhận", icon: Clock }, { key: "CONFIRMED", label: "Xác nhận", icon: CheckCircle2 }, { key: "PREPARING", label: "Pha chế", icon: Flame }, { key: "READY", label: "Sẵn sàng", icon: PackageCheck }, { key: "DELIVERING", label: "Đang giao", icon: Truck }, { key: "COMPLETED", label: "Đã nhận", icon: Check }];
export default function OrdersPage() {
  const [code, setCode] = useState("");
  const [phone, setPhone] = useState("");
  const [order, setOrder] = useState<Order | null>(null);
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [connected, setConnected] = useState(false);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [payment, setPayment] = useState<PaymentIntent | null>(null);
  const [store, setStore] = useState<Store | null>(null);
  const [paymentNotice, setPaymentNotice] = useState("");
  const lookup = useCallback(async (orderCode: string, lookupPhone: string) => {
    setLoading(true); setMessage(""); setPayment(null);
    try { setOrder(await request<Order>("/orders/lookup", { method: "POST", body: JSON.stringify({ orderCode, phone: lookupPhone }) })); }
    catch (error) { setOrder(null); setMessage(error instanceof Error ? error.message : "Không thể tra cứu đơn."); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => {
    const timer = setTimeout(() => {
      const initialCode = new URLSearchParams(window.location.search).get("order") || "";
      const result = new URLSearchParams(window.location.search).get("payment");
      if (result === "waiting") setPaymentNotice("Bạn đã quay về từ VNPay. Trạng thái bên dưới được xác nhận trực tiếp từ cửa hàng; nếu chưa cập nhật, hãy chờ ít phút và tải mới. Không thanh toán lại khi tài khoản đã bị trừ tiền.");
      if (result === "cancelled") setPaymentNotice("Bạn đã rời cổng thanh toán. Hãy kiểm tra trạng thái đơn và tài khoản ngân hàng trước khi thử lại.");
      if (result === "unverified") setPaymentNotice("Chưa xác minh được kết quả từ cổng thanh toán. Hãy tra cứu đơn; không dựa vào địa chỉ trang để xác nhận đã trả tiền.");
      request<Store>("/store").then(setStore).catch(() => {});
      setCode(initialCode);
      try { const saved = JSON.parse(sessionStorage.getItem(`quedrink-order-${initialCode}`) || "null"); if (saved?.phone) { setPhone(saved.phone); void lookup(initialCode, saved.phone); } } catch { /* The form remains available if local storage is unavailable. */ }
    }, 0);
    return () => clearTimeout(timer);
  }, [lookup]);
  const terminal = !!order && ["COMPLETED", "CANCELLED", "REJECTED"].includes(order.orderStatus);
  const trackedCode = order?.orderCode;
  const trackingToken = order?.trackingToken;
  useEffect(() => {
    if (!trackedCode || !trackingToken || terminal) return;
    const stream = new EventSource(`${api}/orders/${encodeURIComponent(trackedCode)}/events?token=${encodeURIComponent(trackingToken)}`);
    stream.onopen = () => setConnected(true);
    stream.onerror = () => setConnected(false);
    stream.addEventListener("status", event => {
      try {
        const update = JSON.parse((event as MessageEvent).data) as Pick<Order, "orderStatus" | "paymentStatus" | "total" | "version">;
        setOrder(current => current ? { ...current, ...update, canCancel: update.paymentStatus === "UNPAID" && ["PENDING", "CONFIRMED"].includes(update.orderStatus) } : current);
      } catch { /* Keep the last valid server state until the next event. */ }
    });
    return () => stream.close();
  }, [trackedCode, trackingToken, terminal]);
  function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); void lookup(code, phone); }
  async function cancel() {
    if (!order) return;
    setBusy(true); setMessage("");
    try { setOrder(await request<Order>("/orders/cancel", { method: "POST", body: JSON.stringify({ orderCode: order.orderCode, phone: order.guestPhone }) })); setConfirmCancel(false); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Không thể hủy đơn."); setConfirmCancel(false); await lookup(order.orderCode, order.guestPhone); }
    finally { setBusy(false); }
  }
  async function startPayment() {
    if (!order) return;
    setBusy(true); setMessage("");
    try { setPayment(await request<PaymentIntent>("/payments/intents", { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ orderCode: order.orderCode, phone: order.guestPhone }) })); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Chưa tạo được giao dịch thanh toán."); }
    finally { setBusy(false); }
  }
  const timeline = steps.filter(s => order?.deliveryType !== "PICKUP" || s.key !== "DELIVERING");
  const currentStep = timeline.findIndex(s => s.key === order?.orderStatus);
  return <main className="orders-page"><SiteHeader active="orders" /><div className="tracking-shell"><div className="page-heading centered"><p className="eyebrow">TỪ QUẦY PHA CHẾ ĐẾN TAY BẠN</p><h1>Ly ngon đang đến đâu?</h1><p>Tra cứu bằng mã đơn và số điện thoại đã đặt hàng.</p></div>
    <form onSubmit={submit} className="panel lookup-form"><label className="field">Mã đơn hàng<input required value={code} onChange={e => setCode(e.target.value.toUpperCase())} maxLength={32} placeholder="QD-…" autoComplete="off" /></label><label className="field">Số điện thoại đặt hàng<input required type="tel" value={phone} onChange={e => setPhone(e.target.value)} maxLength={20} autoComplete="tel" placeholder="0912345678" /></label><button className="button button--primary" disabled={loading}><Search size={18} />{loading ? "Đang tìm…" : "Tra cứu đơn"}</button></form>
    {message && <p className="alert alert--error" role="alert">{message}</p>}
    {paymentNotice && <p className="info-note" role="status">{paymentNotice}</p>}
    {!order && !loading && <div className="tracking-empty"><PackageCheck size={48} /><h2>Mỗi đơn hàng đều có hành trình riêng.</h2><p>Mã đơn xuất hiện sau khi đặt hàng thành công.<br />Bạn cũng có thể tìm lại trong lịch sử tài khoản.</p><Link href="/account" className="text-link">Đến tài khoản <ArrowRight size={16} /></Link></div>}
    {order && <section className="panel tracked-order" aria-live="polite"><div className="tracked-order__heading"><div><small>Mã đơn của bạn</small><h2>{order.orderCode}</h2><p>{new Date(order.createdAt).toLocaleString("vi-VN")}</p></div><span className={`order-status order-status--${order.orderStatus.toLowerCase()}`}>{statusLabels[order.orderStatus]}</span></div>
    <div className="tracking-live"><span className={terminal || connected ? "live-dot" : "live-dot is-offline"} />{terminal ? "Đơn đã kết thúc" : connected ? "Đang cập nhật trực tiếp từ quán" : "Đang kết nối lại… Bạn có thể tải mới thủ công."}<button className="text-link" type="button" disabled={loading} onClick={() => void lookup(order.orderCode, order.guestPhone)}><RefreshCw size={14} /> Tải mới</button></div>
    {["CANCELLED", "REJECTED"].includes(order.orderStatus) ? <div className="cancelled-banner"><XCircle size={25} /><div><h3>{statusLabels[order.orderStatus]}</h3><p>Quán sẽ không tiếp tục xử lý đơn này. Bạn có thể chọn món và đặt đơn mới.</p></div></div> : <ol className="tracking-timeline">{timeline.map((s, i) => <li className={i < currentStep ? "is-done" : i === currentStep ? "is-active" : ""} key={s.key} aria-current={i === currentStep ? "step" : undefined}><span><s.icon size={20} /></span><p>{s.label}</p></li>)}</ol>}
    <div className="tracking-details"><div><h3><MapPin size={18} />{order.deliveryType === "PICKUP" ? "Nhận tại quán" : "Giao tận nơi"}</h3><b>{order.guestName} · {order.guestPhone}</b><p>{order.deliveryAddress}</p>{order.note && <p className="muted">Lời nhắn: {order.note}</p>}</div><div><h3>Thanh toán</h3><b>{paymentLabels[order.paymentStatus] || order.paymentStatus}</b><p>{order.paymentMethod === "COD" ? "Trả khi nhận hàng" : store?.paymentProvider === "MOCK" ? "Thanh toán mô phỏng" : "Thanh toán trực tuyến"}</p>{order.paymentMethod === "BANK_TRANSFER" && order.paymentStatus === "UNPAID" && order.orderStatus === "PENDING" && store?.onlinePaymentEnabled && <button type="button" className="text-link" disabled={busy} onClick={() => void startPayment()}>Tiếp tục thanh toán</button>}</div></div>
    {order.paymentStatus === "REVIEW" && <p className="alert alert--error">Cửa hàng đã ghi nhận tiền cho đơn cần đối soát. Không thanh toán thêm; vui lòng liên hệ cửa hàng để được kiểm tra và hoàn tiền khi cần.</p>}{payment && order.paymentStatus === "UNPAID" && <section className="panel"><h3>{payment.provider === "MOCK" ? "Thanh toán mô phỏng" : payment.isDemo ? "VNPay sandbox — thử nghiệm" : "Thanh toán qua VNPay"}</h3><p>{payment.reference} · {money.format(payment.amount)}</p>{payment.provider === "MOCK" ? <p className="demo-note"><FlaskConical size={24} /> Đây là bản demo; quản trị viên mô phỏng kết quả. Không chuyển tiền thật.</p> : <><p>{payment.isDemo ? "Chỉ dùng thông tin thẻ thử nghiệm do VNPay cung cấp." : "Chỉ thanh toán lại sau khi kiểm tra đơn và xác nhận tài khoản chưa bị trừ tiền."}</p>{payment.checkoutUrl && <a className="button button--primary" href={payment.checkoutUrl}><CreditCard size={18} />{payment.isDemo ? "Mở VNPay sandbox" : "Thanh toán tại VNPay"}</a>}</>}</section>}
    <div className="tracking-items"><h3>Chi tiết ly ngon</h3>{order.items.map((item, index) => <div key={index}><span className="item-count">{item.quantity}×</span><span><b>{item.name}</b><small>{[item.size, item.sugar, item.ice, item.toppings].filter(Boolean).join(" · ")}</small>{item.note && <small>Ghi chú: {item.note}</small>}</span><strong>{money.format(item.total)}</strong></div>)}</div><div className="summary-lines tracking-summary"><p><span>Tiền món</span><b>{money.format(order.subtotal)}</b></p><p><span>Phí giao nhận</span><b>{money.format(order.shippingFee)}</b></p><p><span>Ưu đãi {order.voucherCode}</span><b>−{money.format(order.discount)}</b></p><p className="summary-total"><span>Tổng thanh toán</span><strong>{money.format(order.total)}</strong></p></div>
    <div className="order-actions">{order.canCancel && <button type="button" className="button button--danger" disabled={busy} onClick={() => setConfirmCancel(true)}>Hủy đơn chưa pha chế</button>}{order.orderStatus === "COMPLETED" && <Link href="/account" className="button button--primary">Đánh giá món đã mua <ArrowRight size={16} /></Link>}<Link href="/menu" className="button button--secondary">Chọn món khác</Link></div></section>}
    {confirmCancel && <Modal title="Xác nhận hủy đơn" onClose={() => setConfirmCancel(false)}><div className="confirm-dialog"><XCircle size={36} /><h2>Hủy đơn {order?.orderCode}?</h2><p>Chỉ hủy được đơn chưa pha chế và chưa thanh toán. Mã ưu đãi sẽ được trả lại lượt sử dụng.</p><button className="button button--danger" disabled={busy} onClick={() => void cancel()}>{busy ? "Đang hủy…" : "Xác nhận hủy"}</button><button className="button button--secondary" disabled={busy} onClick={() => setConfirmCancel(false)}>Giữ đơn của tôi</button></div></Modal>}
  </div></main>;
}
