"use client";
import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ArrowRight, Award, Heart, KeyRound, LogIn, LogOut, Package, Star, UserCircle } from "lucide-react";
import { SiteHeader } from "../components/SiteHeader";
import { DrinkVisual } from "../components/DrinkVisual";
import { Modal } from "../components/Modal";
import { clearCustomerSession, readCustomerSession, writeCustomerSession, type CustomerSession } from "../../lib/customer-auth";
import { ApiError, money, request, statusLabels, type Order, type Product } from "../../lib/api";

type Loyalty = { points: number; transactions: { points: number; reason: string; createdAt: string }[] };
type OwnReview = { orderId: string; productId: string; rating: number };
export default function AccountPage() {
  const [session, setSession] = useState<CustomerSession | null>(null);
  const [mode, setMode] = useState<"login" | "register" | "forgot">("login");
  const [orders, setOrders] = useState<Order[]>([]);
  const [favorites, setFavorites] = useState<Product[]>([]);
  const [loyalty, setLoyalty] = useState<Loyalty>({ points: 0, transactions: [] });
  const [reviews, setReviews] = useState<OwnReview[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [dataLoading, setDataLoading] = useState(false);
  const [resetToken, setResetToken] = useState("");
  const [reviewing, setReviewing] = useState<{ orderId: string; productId: string; name: string } | null>(null);
  const [rating, setRating] = useState(5);
  const [reviewError, setReviewError] = useState("");
  const load = useCallback(async () => {
    setDataLoading(true);
    try {
      const [orders, loyalty, ids, products, reviews] = await Promise.all([request<Order[]>("/orders/mine", {}, "customer"), request<Loyalty>("/me/loyalty", {}, "customer"), request<string[]>("/me/favorites", {}, "customer"), request<Product[]>("/products"), request<OwnReview[]>("/me/reviews", {}, "customer")]);
      setOrders(orders); setLoyalty(loyalty); setFavorites(products.filter(p => ids.includes(p.id))); setReviews(reviews);
    } catch (error) { setError(error instanceof Error ? error.message : "Chưa tải được tài khoản."); if (error instanceof ApiError && error.status === 401) { clearCustomerSession(); setSession(null); } }
    finally { setDataLoading(false); }
  }, []);
  useEffect(() => {
    const restore = () => {
      const token = new URLSearchParams(window.location.hash.slice(1)).get("reset");
      if (token) {
        window.history.replaceState(null, "", window.location.pathname);
        if (/^[a-f0-9]{64}$/.test(token)) { clearCustomerSession(); setSession(null); setMode("forgot"); setResetToken(token); setNotice("Nhập mật khẩu mới để hoàn tất khôi phục tài khoản."); }
        else setError("Liên kết khôi phục không hợp lệ. Vui lòng yêu cầu liên kết mới.");
      } else { const active = readCustomerSession(); setSession(active); if (active) void load(); }
    };
    const timer = setTimeout(restore, 0);
    const sync = () => setSession(readCustomerSession());
    window.addEventListener("quedrink-customer", sync);
    window.addEventListener("hashchange", restore);
    return () => { clearTimeout(timer); window.removeEventListener("quedrink-customer", sync); window.removeEventListener("hashchange", restore); };
  }, [load]);
  async function authenticate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(""); setNotice("");
    const fields = new FormData(event.currentTarget);
    try {
      if (mode === "forgot") {
        const result = await request<{ resetToken: string | null; deliveryEnabled: boolean }>("/auth/forgot-password", { method: "POST", body: JSON.stringify({ identity: fields.get("identity") }) });
        setResetToken(result.resetToken || ""); setNotice(result.deliveryEnabled ? "Nếu tài khoản có email, liên kết khôi phục sẽ được gửi đến email đã đăng ký. Hãy kiểm tra cả thư mục Spam." : result.resetToken ? "Chế độ demo: mã phục hồi được điền tự động và có hiệu lực 15 phút." : "Gửi email chưa được cấu hình. Vui lòng liên hệ cửa hàng.");
      } else {
        const body = mode === "login" ? { identity: fields.get("identity"), password: fields.get("password") } : { displayName: fields.get("displayName"), email: fields.get("email"), phone: fields.get("phone") || null, password: fields.get("password") };
        const result = await request<{ accessToken: string; refreshToken: string; expiresIn: number; user: CustomerSession["user"] }>(`/auth/${mode}`, { method: "POST", body: JSON.stringify(body) });
        const active = { ...result, expiresAt: Date.now() + result.expiresIn * 1000 };
        writeCustomerSession(active); setSession(active); await load();
      }
    } catch (error) { setError(error instanceof Error ? error.message : "Không đăng nhập được."); }
    finally { setBusy(false); }
  }
  async function logout() {
    setBusy(true);
    try { const active = readCustomerSession(); if (active) await request("/auth/logout", { method: "POST", body: JSON.stringify({ refreshToken: active.refreshToken }) }); clearCustomerSession(); setSession(null); setOrders([]); setError(""); setNotice("Đã đăng xuất."); }
    catch (error) { setError(error instanceof Error ? error.message : "Chưa đăng xuất được. Hãy thử lại."); }
    finally { setBusy(false); }
  }
  async function password(event: FormEvent<HTMLFormElement>, reset = false) {
    event.preventDefault(); const fields = new FormData(event.currentTarget); setError(""); setNotice("");
    if (fields.get("newPassword") !== fields.get("confirmPassword")) { setError("Hai mật khẩu mới chưa khớp."); return; }
    setBusy(true);
    try {
      await request(`/auth/${reset ? "reset-password" : "change-password"}`, { method: "POST", body: JSON.stringify(reset ? { token: resetToken, newPassword: fields.get("newPassword") } : { currentPassword: fields.get("currentPassword"), newPassword: fields.get("newPassword") }) }, reset ? undefined : "customer");
      clearCustomerSession(); setSession(null); setMode("login"); setResetToken(""); setNotice("Đã đổi mật khẩu. Vui lòng đăng nhập lại.");
    } catch (error) { setError(error instanceof Error ? error.message : "Không đổi được mật khẩu."); }
    finally { setBusy(false); }
  }
  async function submitReview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!reviewing) return; setBusy(true); setReviewError("");
    const fields = new FormData(event.currentTarget);
    try { await request("/me/reviews", { method: "POST", body: JSON.stringify({ orderId: reviewing.orderId, productId: reviewing.productId, rating, comment: fields.get("comment") }) }, "customer"); setReviews(current => [...current, { ...reviewing, rating }]); setReviewing(null); setNotice("Cảm ơn bạn đã chia sẻ cảm nhận!"); }
    catch (error) { setReviewError(error instanceof Error ? error.message : "Chưa gửi được đánh giá."); }
    finally { setBusy(false); }
  }
  return <main className="account-page"><SiteHeader active="account" /><div className="account-shell">
    {error && <p className="alert alert--error" role="alert">{error}</p>}{notice && <p className="alert alert--success" role="status">{notice}</p>}
    {session ? <><section className="account-welcome"><div><p className="eyebrow">GÓC NHỎ CỦA BẠN</p><h1>Chào {session.user.displayName}!</h1><p>{session.user.email || session.user.phone}</p></div><button className="button button--light" disabled={busy} onClick={() => void logout()}><LogOut size={17} /> Đăng xuất</button></section><div className="account-grid"><div className="account-main"><section className="panel"><div className="panel-heading"><h2><Package size={21} /> Đơn hàng của bạn</h2><button className="text-link" onClick={() => void load()} disabled={dataLoading}>Tải mới</button></div>{dataLoading ? <p className="loading-message">Đang tải dữ liệu…</p> : !orders.length ? <div className="small-empty"><p>Bạn chưa có đơn gắn với tài khoản.</p><Link href="/menu" className="text-link">Chọn ly đầu tiên <ArrowRight size={16} /></Link></div> : <div className="account-orders">{orders.map(order => <article key={order.id}><div className="account-order-title"><Link href={`/orders?order=${order.orderCode}`} onClick={() => { try { sessionStorage.setItem(`quedrink-order-${order.orderCode}`, JSON.stringify({ phone: order.guestPhone })); } catch { /* Manual lookup is still available. */ } }}><b>{order.orderCode}</b><small>{new Date(order.createdAt).toLocaleString("vi-VN")}</small></Link><div><b>{money.format(order.total)}</b><span className={`order-status order-status--${order.orderStatus.toLowerCase()}`}>{statusLabels[order.orderStatus]}</span></div></div><p className="muted">{order.items.map(i => `${i.quantity}× ${i.name}`).join(", ")}</p>{order.orderStatus === "COMPLETED" && <div className="review-actions">{Array.from(new Map(order.items.map(i => [i.productId, i])).values()).map(item => reviews.some(r => r.orderId === order.id && r.productId === item.productId) ? <span key={item.productId} className="success-text"><Star size={14} /> Đã đánh giá {item.name}</span> : <button key={item.productId} type="button" className="text-link" onClick={() => { setRating(5); setReviewError(""); setReviewing({ orderId: order.id, productId: item.productId, name: item.name }); }}><Star size={14} /> Đánh giá {item.name}</button>)}</div>}</article>)}</div>}<p className="panel-footnote">Hiển thị tối đa 200 đơn gần nhất. Đăng nhập trước khi đặt để lưu đơn tại đây.</p></section>
    <section className="panel"><div className="panel-heading"><h2><Heart size={21} /> Những vị đã lưu</h2><Link href="/menu" className="text-link">Thêm món</Link></div>{!favorites.length ? <p className="muted">Chạm biểu tượng trái tim ở thực đơn để lưu món yêu thích.</p> : <div className="favorite-list">{favorites.map(p => <Link key={p.id} href={`/menu?product=${encodeURIComponent(p.slug)}`}><DrinkVisual compact name={p.name} categoryName={p.categoryName} imageUrl={p.imageUrl} /><span><b>{p.name}</b><small>{money.format(p.salePrice ?? p.basePrice)}</small></span><ArrowRight size={17} /></Link>)}</div>}</section></div>
    <aside className="account-aside"><section className="loyalty-card"><Award size={32} /><p>Điểm tích lũy</p><strong>{loyalty.points}</strong><p>Mỗi 10.000đ từ đơn hoàn thành được ghi nhận 1 điểm.</p><small>Hiện chưa có chức năng đổi điểm.</small>{loyalty.transactions.length > 0 && <details><summary>Lịch sử điểm gần đây</summary>{loyalty.transactions.map((t, i) => <div key={i}><span>{new Date(t.createdAt).toLocaleDateString("vi-VN")}</span><b>+{t.points}</b></div>)}</details>}</section><section className="panel"><details><summary className="details-heading"><KeyRound size={18} /> Đổi mật khẩu</summary><form onSubmit={e => void password(e)} className="stack-form"><label className="field">Mật khẩu hiện tại<input name="currentPassword" type="password" required maxLength={72} autoComplete="current-password" /></label><label className="field">Mật khẩu mới<input name="newPassword" type="password" required minLength={10} maxLength={72} autoComplete="new-password" /></label><label className="field">Nhập lại mật khẩu mới<input name="confirmPassword" type="password" required minLength={10} maxLength={72} autoComplete="new-password" /></label><button className="button button--primary" disabled={busy}>Lưu mật khẩu</button></form></details></section></aside></div></> :
    <div className="auth-layout"><section className="auth-story"><UserCircle size={48} /><p className="eyebrow">QUÊ DRINKS CLUB</p><h1>Gu riêng của bạn.<br />Một chỗ để lưu.</h1><p>Giữ lại những món yêu thích, theo dõi lịch sử đơn hàng và chia sẻ cảm nhận sau mỗi ly ngon.</p><DrinkVisual name="Trà sữa đường đen" categoryName="Trà sữa" /></section><section className="panel auth-panel"><div className="auth-tabs">{(["login", "register"] as const).map(m => <button key={m} type="button" className={mode === m ? "is-active" : ""} onClick={() => { setMode(m); setError(""); setResetToken(""); setNotice(""); }}>{m === "login" ? "Đăng nhập" : "Đăng ký"}</button>)}</div><h2>{mode === "login" ? "Mừng bạn quay lại!" : mode === "register" ? "Chào thành viên mới." : "Khôi phục mật khẩu"}</h2><p className="muted">{mode === "register" ? "Tạo tài khoản để lưu trọn trải nghiệm của bạn." : "Nhập thông tin tài khoản của bạn bên dưới."}</p>{(mode !== "forgot" || !resetToken) && <form onSubmit={authenticate} className="stack-form" key={mode}>{mode === "register" ? <><label className="field">Họ và tên<input name="displayName" required minLength={2} maxLength={120} autoComplete="name" /></label><label className="field">Email<input name="email" type="email" required maxLength={255} autoComplete="email" /></label><label className="field">Số điện thoại (tùy chọn)<input name="phone" type="tel" pattern="(0|\+84)[0-9]{9,10}" maxLength={20} autoComplete="tel" /></label></> : <label className="field">Email hoặc số điện thoại<input name="identity" required maxLength={255} autoComplete="username" /></label>}{mode !== "forgot" && <label className="field">Mật khẩu<input name="password" type="password" required minLength={mode === "register" ? 10 : 1} maxLength={72} autoComplete={mode === "register" ? "new-password" : "current-password"} />{mode === "register" && <small>Từ 10 ký tự, tối đa 72 byte UTF-8.</small>}</label>}<button className="button button--primary" disabled={busy}><LogIn size={17} />{busy ? "Đang xử lý…" : mode === "login" ? "Đăng nhập" : mode === "register" ? "Tạo tài khoản" : "Gửi liên kết khôi phục"}</button></form>}{mode === "login" && <button type="button" className="text-link" onClick={() => { setMode("forgot"); setError(""); }}>Quên mật khẩu?</button>}{mode === "forgot" && <p className="demo-note">Liên kết khôi phục có hiệu lực 15 phút. Không chia sẻ liên kết hoặc mật khẩu với người khác.</p>}{resetToken && <form onSubmit={e => void password(e, true)} className="stack-form"><h3>Đặt mật khẩu mới</h3><label className="field">Mật khẩu mới<input name="newPassword" type="password" required minLength={10} maxLength={72} autoComplete="new-password" /></label><label className="field">Nhập lại mật khẩu<input name="confirmPassword" type="password" required minLength={10} maxLength={72} autoComplete="new-password" /></label><button className="button button--primary" disabled={busy}>Đặt lại mật khẩu</button></form>}</section></div>}
    {reviewing && <Modal title={`Đánh giá ${reviewing.name}`} onClose={() => { if (!busy) setReviewing(null); }}><h2>Ly {reviewing.name} thế nào?</h2><p className="muted">Chia sẻ cảm nhận từ đơn đã hoàn thành.</p>{reviewError && <p className="alert alert--error" role="alert">{reviewError}</p>}<form className="stack-form" onSubmit={submitReview}><fieldset className="rating-input"><legend>Mức độ hài lòng</legend>{[1, 2, 3, 4, 5].map(n => <label key={n}><input type="radio" name="rating" value={n} checked={rating === n} onChange={() => setRating(n)} /><Star size={28} fill={n <= rating ? "currentColor" : "none"} /><span className="sr-only">{n} sao</span></label>)}</fieldset><label className="field">Cảm nhận của bạn<textarea name="comment" maxLength={1000} rows={4} placeholder="Hương vị, độ ngọt, topping…" /></label><button className="button button--primary" disabled={busy}>{busy ? "Đang gửi…" : "Gửi đánh giá"}</button></form></Modal>}
  </div></main>;
}

