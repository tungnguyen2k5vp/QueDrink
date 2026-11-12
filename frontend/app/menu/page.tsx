"use client";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowRight, Check, Heart, Plus, Search, ShoppingBag, SlidersHorizontal, Sparkles, Star, X } from "lucide-react";
import { SiteHeader } from "../components/SiteHeader";
import { DrinkVisual } from "../components/DrinkVisual";
import { Modal } from "../components/Modal";
import { addToCart, readCart } from "../../lib/cart";
import { readCustomerSession } from "../../lib/customer-auth";
import { money, request, type Category, type Product, type ProductDetail, type Review } from "../../lib/api";

const normalize = (value: string) => value.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replaceAll("đ", "d");

function Customize({ product, close, added }: { product: Product; close: () => void; added: () => void }) {
  const [detail, setDetail] = useState<ProductDetail | null>(null);
  const [reviews, setReviews] = useState<Review[]>([]);
  const [error, setError] = useState("");
  const [size, setSize] = useState("");
  const [sugar, setSugar] = useState("");
  const [ice, setIce] = useState("");
  const [toppings, setToppings] = useState<string[]>([]);
  const [note, setNote] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    request<ProductDetail>(`/products/${encodeURIComponent(product.slug)}`, { signal: controller.signal }).then(value => {
      setDetail(value); setSize(value.sizes[0]?.id || ""); setSugar(value.sugarOptions[0]?.id || ""); setIce(value.iceOptions[0]?.id || "");
    }).catch(error => { if (!controller.signal.aborted) setError(error.message); });
    request<Review[]>(`/products/${product.id}/reviews`, { signal: controller.signal }).then(setReviews).catch(() => {});
    return () => controller.abort();
  }, [product.id, product.slug]);
  const selectedSize = detail?.sizes.find(s => s.id === size);
  const selectedToppings = detail?.toppings.filter(t => toppings.includes(t.id)) || [];
  const current = detail?.product || product;
  const price = (current.salePrice ?? current.basePrice) + (selectedSize?.price || 0) + selectedToppings.reduce((sum, t) => sum + t.price, 0);
  function add() {
    const selectedSugar = detail?.sugarOptions.find(s => s.id === sugar);
    const selectedIce = detail?.iceOptions.find(i => i.id === ice);
    if (!selectedSize || !selectedSugar || !selectedIce) return;
    try {
      addToCart({ productId: product.id, name: current.name, categoryName: current.categoryName, imageUrl: current.imageUrl, price,
        selection: { size: selectedSize, sugar: selectedSugar, ice: selectedIce, toppings: selectedToppings, note: note.trim() || undefined } });
      added(); close();
    } catch (error) { setError(error instanceof Error ? error.message : "Không lưu được giỏ hàng. Hãy kiểm tra quyền lưu trữ của trình duyệt."); }
  }
  return <Modal title={`Tùy chọn ${product.name}`} onClose={close}>
    <div className="customize-heading"><DrinkVisual compact name={product.name} categoryName={product.categoryName} imageUrl={product.imageUrl} /><div><p className="eyebrow text-emerald-800">{product.categoryName}</p><h2>{product.name}</h2><p className="muted">{product.description}</p></div></div>
    {error && <p role="alert" className="alert alert--error">{error}</p>}
    {!detail && !error && <p className="loading-message">Đang tải lựa chọn từ quán…</p>}
    {detail && <div className="customize-options">
      {([{ title: "Kích cỡ", options: detail.sizes, value: size, change: setSize }, { title: "Mức đường", options: detail.sugarOptions, value: sugar, change: setSugar }, { title: "Mức đá", options: detail.iceOptions, value: ice, change: setIce }]).map(group => <fieldset key={group.title} className="option-group"><legend>{group.title} <small>Chọn 1</small></legend><div className="option-chips">{group.options.map(option => <button type="button" key={option.id} className={`chip-button ${group.value === option.id ? "is-active" : ""}`} aria-pressed={group.value === option.id} onClick={() => group.change(option.id)}>{option.name}{"price" in option && Number(option.price) > 0 ? ` +${money.format(Number(option.price))}` : ""}</button>)}</div></fieldset>)}
      {detail.toppings.length > 0 && <fieldset className="option-group"><legend>Topping <small>Tối đa 5 loại</small></legend><div className="topping-grid">{detail.toppings.map(t => <label className={`topping-choice ${toppings.includes(t.id) ? "is-selected" : ""}`} key={t.id}><input type="checkbox" checked={toppings.includes(t.id)} disabled={!toppings.includes(t.id) && toppings.length >= 5} onChange={() => setToppings(current => current.includes(t.id) ? current.filter(id => id !== t.id) : [...current, t.id])} /><span>{t.name}<small>+{money.format(t.price)}</small></span></label>)}</div></fieldset>}
      <label className="field">Ghi chú cho món<textarea value={note} onChange={e => setNote(e.target.value)} maxLength={300} rows={2} placeholder="Ví dụ: không dùng ống hút…" /></label>
    </div>}
    <div className="customize-submit"><div><small>Tạm tính mỗi ly</small><strong>{money.format(price)}</strong></div><button className="button button--primary" type="button" disabled={!selectedSize || !sugar || !ice} onClick={add}><ShoppingBag size={18} /> Thêm vào giỏ</button></div>
    <details className="product-reviews"><summary><Star size={15} /> Đánh giá từ khách đã mua ({reviews.length})</summary>{!reviews.length ? <p className="muted">Chưa có đánh giá. Bạn có thể đánh giá sau khi nhận hàng.</p> : reviews.map((r, i) => <article key={i}><b>{"★".repeat(r.rating)}{"☆".repeat(5 - r.rating)}</b><small>{new Date(r.createdAt).toLocaleDateString("vi-VN")} · Đã mua hàng</small><p>{r.comment}</p></article>)}</details>
  </Modal>;
}

export default function MenuPage() {
  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [favoriteOnly, setFavoriteOnly] = useState(false);
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState("");
  const [priceRange, setPriceRange] = useState("all");
  const [sort, setSort] = useState("popular");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [cart, setCart] = useState({ count: 0, total: 0 });
  const [selected, setSelected] = useState<Product | null>(null);
  const [savingFavorite, setSavingFavorite] = useState("");
  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true); setError("");
    try {
      const [products, categories] = await Promise.all([request<Product[]>("/products", { signal }), request<Category[]>("/categories", { signal })]);
      setProducts(products); setCategories(categories);
      const initialProduct = new URLSearchParams(window.location.search).get("product");
      if (initialProduct) setSelected(products.find(p => p.slug === initialProduct) || null);
    } catch (error) { if (!signal?.aborted) setError(error instanceof Error ? error.message : "Không tải được thực đơn."); }
    finally { if (!signal?.aborted) setLoading(false); }
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => { setCategory(new URLSearchParams(window.location.search).get("category") || ""); void load(controller.signal);
      if (readCustomerSession()) request<string[]>("/me/favorites", {}, "customer").then(setFavorites).catch(() => {});
    }, 0);
    const sync = () => { const items = readCart(); setCart({ count: items.reduce((n, i) => n + i.quantity, 0), total: items.reduce((n, i) => n + i.quantity * i.price, 0) }); };
    sync(); window.addEventListener("quedrink-cart", sync); window.addEventListener("storage", sync);
    return () => { clearTimeout(timer); controller.abort(); window.removeEventListener("quedrink-cart", sync); window.removeEventListener("storage", sync); };
  }, [load]);
  useEffect(() => { if (!notice) return; const timer = setTimeout(() => setNotice(""), 3600); return () => clearTimeout(timer); }, [notice]);
  const shown = useMemo(() => products.filter(p => {
    const price = p.salePrice ?? p.basePrice;
    return (!category || categories.some(c => c.slug === category && c.id === p.categoryId)) && (!favoriteOnly || favorites.includes(p.id)) &&
      (!query.trim() || normalize(p.name + " " + p.description).includes(normalize(query.trim()))) &&
      (priceRange === "all" || priceRange === "under25" && price < 25000 || priceRange === "25to30" && price >= 25000 && price <= 30000 || priceRange === "over30" && price > 30000);
  }).sort((a, b) => sort === "priceAsc" ? (a.salePrice ?? a.basePrice) - (b.salePrice ?? b.basePrice) : sort === "priceDesc" ? (b.salePrice ?? b.basePrice) - (a.salePrice ?? a.basePrice) : sort === "name" ? a.name.localeCompare(b.name, "vi") : Number(b.bestSeller) - Number(a.bestSeller)), [products, categories, category, favoriteOnly, favorites, query, priceRange, sort]);
  async function favorite(product: Product) {
    if (!readCustomerSession()) { setNotice("Đăng nhập ở mục Tài khoản để lưu món yêu thích nhé."); return; }
    setSavingFavorite(product.id);
    try { const removing = favorites.includes(product.id); await request(`/me/favorites/${product.id}`, { method: removing ? "DELETE" : "POST" }, "customer"); setFavorites(current => removing ? current.filter(id => id !== product.id) : [...current, product.id]); }
    catch (error) { setNotice(error instanceof Error ? error.message : "Chưa lưu được món yêu thích."); }
    finally { setSavingFavorite(""); }
  }
  return <main className="menu-page"><SiteHeader active="menu" />
    {notice && <div className="toast" role="status"><Check size={18} /><span>{notice}</span></div>}
    <div className="menu-container"><section className="menu-hero"><div className="menu-hero__content"><p className="eyebrow">THE FRESH MENU / QUÊ DRINKS</p><h1>Mỗi ngày một gu.<br /><em>Mỗi ly một niềm vui.</em></h1><p className="menu-hero__lede">Một chút trà thơm, một chút topping. Tạo ly đồ uống theo đúng cách bạn thích.</p><div className="hero-pills"><span><Sparkles size={14} /> Pha mới theo đơn</span><span><SlidersHorizontal size={14} /> Tùy chọn đá & đường</span></div></div><div className="menu-hero__photo"><DrinkVisual name="Trà sữa đường đen" categoryName="Trà sữa" priority /></div></section>
    <section className="menu-controls" aria-label="Tìm và lọc đồ uống"><label className="search-field"><Search size={20} /><span className="sr-only">Tìm đồ uống</span><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Hôm nay bạn muốn uống gì?" maxLength={100} />{query && <button type="button" onClick={() => setQuery("")} aria-label="Xóa tìm kiếm"><X size={18} /></button>}</label><div className="filter-scroll" aria-label="Danh mục"><button className={!category ? "is-active" : ""} aria-pressed={!category} onClick={() => setCategory("")}>Tất cả</button>{categories.map(c => <button key={c.id} className={category === c.slug ? "is-active" : ""} aria-pressed={category === c.slug} onClick={() => setCategory(c.slug)}>{c.name}</button>)}</div><div className="menu-filter-row"><label>Khoảng giá<select value={priceRange} onChange={e => setPriceRange(e.target.value)}><option value="all">Tất cả mức giá</option><option value="under25">Dưới 25.000đ</option><option value="25to30">25.000đ – 30.000đ</option><option value="over30">Trên 30.000đ</option></select></label><label>Sắp xếp<select value={sort} onChange={e => setSort(e.target.value)}><option value="popular">Món bán chạy</option><option value="priceAsc">Giá tăng dần</option><option value="priceDesc">Giá giảm dần</option><option value="name">Tên A – Z</option></select></label><button className={`favorite-filter ${favoriteOnly ? "is-active" : ""}`} onClick={() => setFavoriteOnly(!favoriteOnly)} aria-pressed={favoriteOnly}><Heart size={17} fill={favoriteOnly ? "currentColor" : "none"} /> Món đã lưu</button></div></section>
    <div className="menu-section-title"><div><p className="eyebrow">CHỌN VỊ BẠN YÊU</p><h2>Món ngon chờ bạn chọn</h2></div><span>{loading ? "Đang pha thực đơn…" : `${shown.length} món`}</span></div>
    <section className="product-grid" aria-busy={loading} aria-label="Danh sách đồ uống">{loading ? Array.from({ length: 8 }, (_, i) => <div key={i} className="product-card product-card--skeleton"><div className="skeleton h-60" /><div className="p-5 space-y-3"><div className="skeleton h-5 w-3/4" /><div className="skeleton h-4 w-full" /></div></div>) : error ? <div className="empty-state" role="alert"><h2>Thực đơn chưa sẵn sàng</h2><p>{error}</p><button className="button button--primary" onClick={() => void load()}>Thử lại</button></div> : !shown.length ? <div className="empty-state"><Search size={30} /><h2>Chưa tìm thấy món phù hợp</h2><p>Thử từ khóa khác hoặc xem toàn bộ thực đơn nhé.</p><button className="button button--secondary" onClick={() => { setQuery(""); setCategory(""); setPriceRange("all"); setFavoriteOnly(false); }}>Xóa bộ lọc</button></div> : shown.map(product => <article className="product-card" key={product.id}><div className="product-card__visual"><button className="product-image-button" onClick={() => setSelected(product)} aria-label={`Xem ${product.name}`}><DrinkVisual categoryName={product.categoryName} name={product.name} imageUrl={product.imageUrl} /></button>{(product.bestSeller || product.salePrice !== null) && <span className="product-card__badge">{product.salePrice !== null ? "Giá yêu thương" : "Được yêu thích"}</span>}<button disabled={savingFavorite === product.id} className={`favorite-button ${favorites.includes(product.id) ? "is-saved" : ""}`} aria-label={`${favorites.includes(product.id) ? "Bỏ lưu" : "Lưu"} ${product.name}`} aria-pressed={favorites.includes(product.id)} onClick={() => void favorite(product)}><Heart size={19} fill={favorites.includes(product.id) ? "currentColor" : "none"} /></button></div><div className="product-card__content"><p className="product-card__category">{product.categoryName}</p><h3><button onClick={() => setSelected(product)}>{product.name}</button></h3><p className="product-card__description">{product.description}</p><div className="product-card__footer"><div className="product-price"><strong>{money.format(product.salePrice ?? product.basePrice)}</strong>{product.salePrice !== null && <del>{money.format(product.basePrice)}</del>}</div><button className="add-button" onClick={() => setSelected(product)} aria-label={`Thêm ${product.name}`}><Plus size={17} /> Chọn món</button></div></div></article>)}</section>
    </div>{selected && <Customize key={selected.id} product={selected} close={() => setSelected(null)} added={() => setNotice(`Đã thêm ${selected.name} vào giỏ.`)} />}
    {cart.count > 0 && <Link href="/cart" className="floating-cart"><span className="flex items-center gap-3"><ShoppingBag size={22} /><span><strong>{cart.count} ly trong giỏ</strong><small className="block">Sẵn sàng đặt món?</small></span></span><span className="flex items-center gap-3"><b>{money.format(cart.total)}</b><ArrowRight size={20} /></span></Link>}
  </main>;
}
