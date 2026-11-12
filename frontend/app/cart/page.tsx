"use client";
import Link from "next/link";
import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { ArrowLeft, ArrowRight, Banknote, CheckCircle2, CreditCard, FlaskConical, MapPin, Minus, Plus, ShoppingBag, TicketPercent, Trash2, Truck } from "lucide-react";
import { SiteHeader } from "../components/SiteHeader";
import { DrinkVisual } from "../components/DrinkVisual";
import { cartKey, describeSelection, readCart, writeCart, type CartItem } from "../../lib/cart";
import { readCustomerSession } from "../../lib/customer-auth";
import { money, orderItems, request, type Order, type PaymentIntent, type Quote, type Store } from "../../lib/api";

export default function CartPage() {
  const [items, setItems] = useState<CartItem[]>([]);
  const [store, setStore] = useState<Store | null>(null);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [address, setAddress] = useState("");
  const [note, setNote] = useState("");
  const [deliveryType, setDeliveryType] = useState("DELIVERY");
  const [paymentMethod, setPaymentMethod] = useState("COD");
  const [voucherInput, setVoucherInput] = useState("");
  const [voucher, setVoucher] = useState("");
  const [quoted, setQuoted] = useState<{ key: string; data: Quote } | null>(null);
  const [quoteError, setQuoteError] = useState("");
  const [quoteRetry, setQuoteRetry] = useState(0);
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState("");
  const [receipt, setReceipt] = useState<Order | null>(null);
  const [payment, setPayment] = useState<PaymentIntent | null>(null);
  const pending = useRef<{ key: string; payload: string } | null>(null);
  useEffect(() => {
    const sync = () => setItems(readCart());
    const timer = setTimeout(() => {
      sync(); const active = readCustomerSession(); setName(active?.user.displayName || ""); setPhone(active?.user.phone || "");
      try { if (!readCart().length) setReceipt(JSON.parse(sessionStorage.getItem("quedrink-last-receipt") || "null")); } catch { /* Manual order lookup remains available. */ }
      try { pending.current = JSON.parse(sessionStorage.getItem("quedrink-checkout-pending") || "null"); } catch { /* A damaged draft can be replaced. */ }
      request<Store>("/store").then(setStore).catch(() => {});
    }, 0);
    window.addEventListener("quedrink-cart", sync); window.addEventListener("storage", sync);
    return () => { clearTimeout(timer); window.removeEventListener("quedrink-cart", sync); window.removeEventListener("storage", sync); };
  }, []);
  const quoteKey = useMemo(() => JSON.stringify({ items: orderItems(items), deliveryType, voucherCode: voucher || undefined }), [items, deliveryType, voucher]);
  const quote = quoted?.key === quoteKey ? quoted.data : null;
  useEffect(() => {
    if (!items.length) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setQuoteError("");
      request<Quote>("/orders/quote", { method: "POST", body: quoteKey, signal: controller.signal }).then(data => {
        if (!controller.signal.aborted) { setQuoted({ key: quoteKey, data }); setQuoteError(""); }
      }).catch(error => { if (!controller.signal.aborted) { setQuoted(null); setQuoteError(error.message); } });
    }, 250);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [quoteKey, quoteRetry, items.length]);
  const count = items.reduce((n, i) => n + i.quantity, 0);
  function update(next: CartItem[]) {
    try { writeCart(next); setItems(next); setMessage(""); } catch { setMessage("Không lưu được giỏ hàng. Vui lòng kiểm tra quyền lưu trữ trình duyệt."); }
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!quote || sending || !items.length) return;
    const payload = JSON.stringify({ name, phone, address: deliveryType === "DELIVERY" ? address : undefined, note, deliveryType, paymentMethod, voucherCode: voucher || undefined, items: orderItems(items) });
    if (!pending.current || pending.current.payload !== payload) pending.current = { key: crypto.randomUUID(), payload };
    const key = pending.current.key;
    setSending(true); setMessage("");
    try {
      sessionStorage.setItem("quedrink-checkout-pending", JSON.stringify(pending.current));
      const order = await request<Order>("/orders", { method: "POST", headers: { "Idempotency-Key": key }, body: payload }, readCustomerSession() ? "customer" : undefined);
      setReceipt(order);
      try { sessionStorage.setItem(`quedrink-order-${order.orderCode}`, JSON.stringify({ phone })); sessionStorage.setItem("quedrink-last-receipt", JSON.stringify(order)); }
      catch { setMessage(`Đơn ${order.orderCode} đã tạo. Hãy lưu mã đơn vì trình duyệt không lưu được thông tin tra cứu.`); }
      try { writeCart([]); setItems([]); pending.current = null; sessionStorage.removeItem("quedrink-checkout-pending"); }
      catch { setItems([]); setMessage(`Đơn ${order.orderCode} đã tạo. Trình duyệt chưa xóa được giỏ; hãy tra cứu đơn trước khi đặt thêm.`); }
      setQuoted(null);
      if (paymentMethod === "BANK_TRANSFER" && order.paymentStatus !== "PAID") {
        try { setPayment(await request<PaymentIntent>("/payments/intents", { method: "POST", headers: { "Idempotency-Key": key + "_pay" }, body: JSON.stringify({ orderCode: order.orderCode, phone }) })); }
        catch (error) { setMessage(`Đơn đã tạo. ${error instanceof Error ? error.message : "Chưa tạo được giao dịch thanh toán."} Bạn có thể thử lại tại trang tra cứu.`); }
      }
    } catch (error) { setMessage(error instanceof Error ? error.message : "Chưa nhận được phản hồi. Bấm thử lại để kiểm tra cùng yêu cầu."); }
    finally { setSending(false); }
  }
  return <main className="cart-page"><SiteHeader active="cart" /><div className="cart-container">
    <Link href="/menu" className="back-link"><ArrowLeft size={16} /> Tiếp tục chọn món</Link>
    <div className="page-heading"><p className="eyebrow">MỘT CHÚT NGON ĐANG CHỜ</p><h1>Giỏ nhỏ, niềm vui to.</h1><p>{count ? `${count} ly đã chọn · Kiểm tra lần cuối trước khi đặt nhé.` : "Ly yêu thích tiếp theo của bạn đang ở thực đơn."}</p></div>
    {message && <p className="alert alert--error" role="alert">{message}</p>}
    {receipt && !items.length ? <section className="order-receipt"><span className="receipt-check"><CheckCircle2 size={38} /></span><p className="eyebrow">QUÁN ĐÃ NHẬN ĐƠN</p><h2>Cảm ơn {receipt.guestName}!</h2><p>Lưu mã này để theo dõi ly đồ uống của bạn.</p><code>{receipt.orderCode}</code><div className="receipt-facts"><span>{receipt.deliveryType === "PICKUP" ? "Nhận tại quán" : "Giao tận nơi"}<b>{receipt.deliveryAddress}</b></span><span>Tổng đơn<b>{money.format(receipt.total)}</b></span></div>{payment && <section className="panel"><h3>{payment.provider === "MOCK" ? "Thanh toán mô phỏng" : payment.isDemo ? "VNPay sandbox — thử nghiệm" : "Thanh toán qua VNPay"}</h3><p>{payment.reference} · {money.format(payment.amount)}</p>{payment.provider === "MOCK" ? <p className="demo-note"><FlaskConical size={20} /> Quản trị viên mô phỏng kết quả. Không chuyển tiền thật.</p> : <><p>{payment.isDemo ? "Chỉ dùng thẻ thử nghiệm do VNPay cung cấp; không nhập thông tin thẻ thật." : "Bạn sẽ chuyển đến cổng VNPay để hoàn tất thanh toán. Cửa hàng cập nhật khi nhận được xác nhận từ cổng."}</p>{payment.checkoutUrl && <a className="button button--primary" href={payment.checkoutUrl}><CreditCard size={18} />{payment.isDemo ? "Mở VNPay sandbox" : "Thanh toán tại VNPay"}</a>}</>}</section>}<Link className="button button--primary" href={`/orders?order=${receipt.orderCode}`}>Theo dõi đơn của tôi <ArrowRight size={17} /></Link><Link className="text-link" href="/menu">Chọn thêm đồ uống</Link></section> : !items.length ? <section className="empty-state empty-cart"><ShoppingBag size={48} /><h2>Giỏ hàng đang đợi một ly ngon</h2><p>Chọn vài món, thêm topping và tùy chỉnh đúng gu của bạn.</p><Link className="button button--primary" href="/menu">Khám phá thực đơn <ArrowRight size={16} /></Link></section> :
    <form className="checkout-layout" onSubmit={submit}><div className="checkout-main"><section className="panel"><div className="panel-heading"><h2>Các món đã chọn</h2><span>{count} ly</span></div>{items.map((item, index) => <article className="checkout-item" key={cartKey(item)}><DrinkVisual compact name={item.name} categoryName={item.categoryName} imageUrl={item.imageUrl} /><div className="checkout-item__info"><small>{item.categoryName}</small><h3>{item.name}</h3><p>{describeSelection(item)}</p><b>{money.format(item.price)}</b><small>Giá lưu trong giỏ; tổng xác nhận từ quán ở bên dưới.</small></div><div className="checkout-item__actions"><div className="quantity-control"><button type="button" aria-label={`Giảm ${item.name}`} disabled={sending || item.quantity <= 1} onClick={() => update(items.map((i, n) => n === index ? { ...i, quantity: i.quantity - 1 } : i))}><Minus size={15} /></button><output>{item.quantity}</output><button type="button" aria-label={`Tăng ${item.name}`} disabled={sending || item.quantity >= 20} onClick={() => update(items.map((i, n) => n === index ? { ...i, quantity: i.quantity + 1 } : i))}><Plus size={15} /></button></div><button type="button" className="remove-item" disabled={sending} aria-label={`Xóa ${item.name}`} onClick={() => update(items.filter((_, n) => n !== index))}><Trash2 size={16} /> Xóa</button></div></article>)}<Link href="/menu" className="text-link"><Plus size={16} /> Chọn thêm món</Link></section>
    <section className="panel"><div className="panel-heading"><h2><MapPin size={20} /> Thông tin nhận hàng</h2></div><fieldset disabled={sending} className="checkout-fields"><legend className="sr-only">Thông tin người nhận</legend><div className="choice-grid">{[{ value: "DELIVERY", label: "Giao tận nơi", icon: Truck }, { value: "PICKUP", label: "Nhận tại quán", icon: ShoppingBag }].map(c => <label className={`choice-card ${deliveryType === c.value ? "is-selected" : ""}`} key={c.value}><input type="radio" name="delivery" value={c.value} checked={deliveryType === c.value} onChange={() => setDeliveryType(c.value)} /><c.icon size={20} />{c.label}</label>)}</div><div className="form-grid"><label className="field">Họ tên người nhận *<input value={name} onChange={e => setName(e.target.value)} required minLength={2} maxLength={120} autoComplete="name" placeholder="Nguyễn Văn An" /></label><label className="field">Số điện thoại *<input value={phone} onChange={e => setPhone(e.target.value)} required type="tel" pattern="(0|\+84)[0-9]{9,10}" maxLength={20} autoComplete="tel" placeholder="0912345678" /></label></div>{deliveryType === "DELIVERY" ? <label className="field">Địa chỉ giao hàng *<textarea value={address} onChange={e => setAddress(e.target.value)} required maxLength={500} rows={2} autoComplete="street-address" placeholder="Số nhà, đường, phường/xã, quận/huyện…" /></label> : <p className="info-note"><MapPin size={18} /><span>Điểm nhận: {store?.address || "Đang tải địa chỉ cửa hàng…"}</span></p>}<label className="field">Lời nhắn cho quán<textarea value={note} onChange={e => setNote(e.target.value)} maxLength={500} rows={2} placeholder="Ví dụ: gọi trước khi giao…" /></label></fieldset></section></div>
    <aside className="panel checkout-summary"><div className="panel-heading"><h2>Hoàn tất đơn hàng</h2></div><section className="voucher-box"><label className="field" htmlFor="voucher"><span><TicketPercent size={17} /> Mã ưu đãi</span></label><div className="input-action"><input id="voucher" value={voucherInput} onChange={e => setVoucherInput(e.target.value.toUpperCase())} maxLength={40} placeholder="WELCOME10" disabled={sending} /><button type="button" className="button button--secondary" disabled={sending} onClick={() => setVoucher(voucherInput.trim())}>Áp dụng</button></div>{voucher && <button className="text-link" type="button" disabled={sending} onClick={() => { setVoucher(""); setVoucherInput(""); }}>Bỏ mã {voucher}</button>}{quote?.voucherCode && <p className="success-text">Đã áp dụng {quote.voucherCode}</p>}</section>
    <fieldset className="payment-choices" disabled={sending}><legend>Thanh toán</legend><label><input type="radio" name="payment" checked={paymentMethod === "COD"} onChange={() => setPaymentMethod("COD")} /><Banknote size={20} /><span>Trả khi nhận hàng<small>Tiền mặt khi giao hoặc nhận tại quán</small></span></label>{store?.onlinePaymentEnabled && <label><input type="radio" name="payment" checked={paymentMethod === "BANK_TRANSFER"} onChange={() => setPaymentMethod("BANK_TRANSFER")} /><CreditCard size={20} /><span>{store.paymentProvider === "MOCK" ? "Thanh toán mô phỏng" : "Thanh toán qua VNPay"}<small>{store.paymentMode === "LIVE" ? "ATM, thẻ hoặc QR tại cổng VNPay" : "Thử nghiệm · không dùng tiền hoặc thẻ thật"}</small></span></label>}</fieldset>
    {quoteError && <div className="alert alert--error" role="alert">{quoteError}<button type="button" className="text-link" onClick={() => setQuoteRetry(n => n + 1)}>Thử lấy báo giá lại</button></div>}
    <div className="summary-lines" aria-live="polite"><p><span>Tiền món</span><b>{quote ? money.format(quote.subtotal) : "—"}</b></p><p><span>{deliveryType === "PICKUP" ? "Phí nhận tại quán" : "Phí giao hàng"}</span><b>{quote ? quote.shippingFee === 0 ? "Miễn phí" : money.format(quote.shippingFee) : "—"}</b></p><p><span>Ưu đãi</span><b className="success-text">{quote ? `−${money.format(quote.discount)}` : "—"}</b></p>{quote && store && deliveryType === "DELIVERY" && quote.subtotal < store.freeShippingFrom && <small className="shipping-hint">Thêm {money.format(store.freeShippingFrom - quote.subtotal)} tiền món để được miễn phí giao hàng.</small>}<p className="summary-total"><span>Tổng thanh toán</span><strong>{quote ? money.format(quote.total) : "Đang cập nhật…"}</strong></p></div>
    <button type="submit" className="button button--primary checkout-confirm" disabled={sending || !quote}>{sending ? "Đang gửi đơn…" : "Xác nhận đặt hàng"}<ArrowRight size={18} /></button><p className="checkout-footnote">Tổng tiền được xác nhận từ cửa hàng. Đơn được xử lý sau khi quán tiếp nhận.</p></aside></form>}
  </div></main>;
}
