import type { Metadata } from "next";
import "./globals.css";
import { Providers } from "./providers";

const storeName = process.env.NEXT_PUBLIC_STORE_NAME || "QueDrink";
export const metadata: Metadata = {
  title: { default: `${storeName} | Đồ uống giao tận nhà`, template: `%s | ${storeName}` },
  description: "Đặt đồ uống địa phương nhanh chóng, tươi ngon, giao hàng tận nơi chỉ trong vài bước.",
  robots: { index: true, follow: true }
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="vi" data-scroll-behavior="smooth">
      <body>
        <a href="#page-content" className="skip-link">Đến nội dung chính</a>
        <div id="page-content" tabIndex={-1}><Providers>{children}</Providers></div>
        <footer className="site-footer"><div><strong>Quê Drinks<span> • Pha theo gu, vui theo cách của bạn.</span></strong><nav aria-label="Liên kết cuối trang"><a href="/menu">Thực đơn</a><a href="/orders">Tra cứu đơn</a><a href="/admin/login">Dành cho cửa hàng</a></nav><small>Vui lòng kiểm tra số tiền, trạng thái đơn và chế độ thanh toán trước khi xác nhận.</small></div></footer>
      </body>
    </html>
  );
}
