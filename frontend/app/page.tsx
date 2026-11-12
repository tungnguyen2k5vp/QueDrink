import Link from "next/link";
import { ArrowRight, CheckCircle2, Coffee, Heart, ShieldCheck, Sparkles, Truck, SlidersHorizontal } from "lucide-react";
import { DrinkVisual } from "./components/DrinkVisual";
import { SiteHeader } from "./components/SiteHeader";

const storeName = process.env.NEXT_PUBLIC_STORE_NAME || "Quê Drinks";

const categories = [
  { label: "Trà sữa", description: "Béo thơm, topping đầy đặn", href: "/menu?category=tra-sua", name: "Trà sữa truyền thống" },
  { label: "Trà trái cây", description: "Tươi mát mỗi ngày", href: "/menu?category=tra-hoa-qua", name: "Trà đào cam sả" },
  { label: "Cà phê", description: "Đậm vị hạt Việt", href: "/menu?category=ca-phe", name: "Cà phê sữa" }
];

export default function HomePage() {
  return (
    <main className="landing-page">
      <SiteHeader active="home" />
      <div className="landing-shell">
        <section className="landing-hero" aria-labelledby="landing-title">
          <div className="landing-hero__copy">
            <p className="landing-kicker"><Sparkles size={16} /> Pha tươi theo gu của bạn</p>
            <h1 id="landing-title">Một ngụm <em>thật đã,</em><br />một ngày thật vui.</h1>
            <p className="landing-hero__lede">Từ trà sữa béo thơm đến cà phê đậm vị, {storeName} pha từng ly theo mức đá, đường và topping bạn yêu thích.</p>
            <div className="landing-hero__actions">
              <Link href="/menu" className="landing-button landing-button--accent">Chọn món ngay <ArrowRight size={18} /></Link>
              <Link href="/orders" className="landing-button landing-button--quiet"><Truck size={18} /> Theo dõi đơn</Link>
            </div>
            <ul className="landing-proof" aria-label="Cam kết phục vụ">
              <li><CheckCircle2 size={17} /> Pha mới khi có đơn</li>
              <li><SlidersHorizontal size={17} /> Đúng gu của bạn</li>
              <li><ShieldCheck size={17} /> Giá rõ ràng</li>
            </ul>
          </div>
          <div className="landing-hero__scene" aria-label="Minh hoạ đồ uống nổi bật">
            <span className="landing-orbit landing-orbit--one" />
            <span className="landing-orbit landing-orbit--two" />
            <div className="landing-drink-card landing-drink-card--main"><DrinkVisual categoryName="Trà sữa" name="Trà sữa đường đen" priority /></div>
            <div className="landing-float landing-float--rating"><SlidersHorizontal size={17} /> Ít đường, nhiều niềm vui</div>
            <div className="landing-float landing-float--fresh"><Sparkles size={17} /> Pha tươi mỗi ngày</div>
            <div className="landing-float landing-float--price">Từ <strong>18.000đ</strong></div>
          </div>
        </section>

        <section className="landing-category-section" aria-labelledby="category-title">
          <div className="landing-section-heading">
            <div><p>KHÁM PHÁ HƯƠNG VỊ</p><h2 id="category-title">Hôm nay bạn thèm gì?</h2></div>
            <Link href="/menu">Xem toàn bộ thực đơn <ArrowRight size={17} /></Link>
          </div>
          <div className="landing-category-grid">
            {categories.map((category) => (
              <Link key={category.label} href={category.href} className="landing-category-card">
                <div className="landing-category-card__visual"><DrinkVisual categoryName={category.label === "Trà trái cây" ? "Trà hoa quả" : category.label} name={category.name} compact /></div>
                <div><h3>{category.label}</h3><p>{category.description}</p></div><ArrowRight size={19} />
              </Link>
            ))}
          </div>
        </section>

        <section className="landing-story">
          <div className="landing-story__visual"><div className="landing-story__mark"><Coffee size={42} /></div><span>QUÊ DRINKS</span><small>PHA TƯƠI · GIAO NHANH</small></div>
          <div className="landing-story__copy"><p className="landing-kicker landing-kicker--dark"><Heart size={16} /> Vị ngon thật gần</p><h2>Một khoảng nghỉ, thật nhiều vị ngon.</h2><p>Thưởng cho mình một ly yêu thích giữa ngày bận rộn. Chọn hương vị thân quen, thêm chút topping và để chúng mình pha theo gu của bạn.</p><div className="landing-story__facts"><span><strong>5</strong> nhóm hương vị</span><span><strong>5</strong> mức đá & đường</span><span><strong>Tùy bạn</strong> giao tận nơi hoặc nhận tại quán</span></div><Link href="/menu" className="landing-text-link">Tự tạo ly uống của bạn <ArrowRight size={17} /></Link></div>
        </section>

        <section className="landing-steps" aria-labelledby="steps-title">
          <div className="landing-section-heading"><div><p>NHANH GỌN, RÕ RÀNG</p><h2 id="steps-title">Một ly ngon chỉ với 3 bước</h2></div></div>
          <ol><li><b>01</b><div><h3>Chọn món</h3><p>Lướt thực đơn và lọc theo đúng gu của bạn.</p></div></li><li><b>02</b><div><h3>Tùy chỉnh</h3><p>Chọn size, đá, đường và topping từ quán.</p></div></li><li><b>03</b><div><h3>Thưởng thức</h3><p>Theo dõi đơn trực tiếp đến khi nhận ly nước.</p></div></li></ol>
        </section>

        <section className="landing-cta"><div><p>ĐÃ ĐẾN LÚC TỰ THƯỞNG</p><h2>Chọn một hương vị thật hợp hôm nay.</h2></div><Link href="/menu" className="landing-button landing-button--light">Mở thực đơn <ArrowRight size={18} /></Link></section>
      </div>
    </main>
  );
}
