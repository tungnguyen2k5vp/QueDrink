"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Coffee, ShoppingBag, UtensilsCrossed, Truck, ShieldCheck, Home, UserCircle } from "lucide-react";
import { readCart } from "../../lib/cart";

type SiteHeaderProps = { active?: "home" | "menu" | "orders" | "cart" | "account" };

export function SiteHeader({ active }: SiteHeaderProps) {
  const [count, setCount] = useState(0);
  const syncCart = useCallback(() => {
    setCount(readCart().reduce((total, item) => total + item.quantity, 0));
  }, []);

  useEffect(() => {
    const initialSync = window.setTimeout(syncCart, 0);
    window.addEventListener("quedrink-cart", syncCart);
    window.addEventListener("storage", syncCart);
    return () => {
      window.clearTimeout(initialSync);
      window.removeEventListener("quedrink-cart", syncCart);
      window.removeEventListener("storage", syncCart);
    };
  }, [syncCart]);

  return (
    <>
      <header className="site-header">
        <div className="site-header__inner">
          <Link href="/" className="brand-lockup" aria-label="Que Drinks - Trang chủ">
            <span className="brand-mark">
              <Coffee className="w-6 h-6 stroke-[2.5]" />
            </span>
            <span className="brand-name">
              Quê Drinks
              <span className="brand-tag">Pha Tươi</span>
            </span>
          </Link>
          <nav className="primary-nav" aria-label="Điều hướng chính">
            <Link href="/" aria-current={active === "home" ? "page" : undefined}>
              <Home className="w-4 h-4" />
              <span>Trang chủ</span>
            </Link>
            <Link href="/menu" aria-current={active === "menu" ? "page" : undefined}>
              <UtensilsCrossed className="w-4 h-4" />
              <span>Thực đơn</span>
            </Link>
            <Link href="/orders" aria-current={active === "orders" ? "page" : undefined}>
              <Truck className="w-4 h-4" />
              <span>Theo dõi đơn</span>
            </Link>
            <Link href="/account" aria-current={active === "account" ? "page" : undefined}>
              <UserCircle className="w-4 h-4" />
              <span>Tài khoản</span>
            </Link>
            <Link href="/admin/login">
              <ShieldCheck className="w-4 h-4" />
              <span>Quản trị</span>
            </Link>
          </nav>
          <Link href="/cart" className="header-cart" aria-current={active === "cart" ? "page" : undefined}>
            <ShoppingBag className="w-4 h-4" />
            <span className="header-cart__label">Giỏ hàng</span>
            <span className="cart-count" aria-label={`${count} món trong giỏ`}>{count}</span>
          </Link>
        </div>
      </header>

      {/* Navigation Chân trang cho Di động */}
      <nav className="bottom-nav" aria-label="Điều hướng di động">
        <Link href="/" aria-current={active === "home" ? "page" : undefined}>
          <Home />
          <span>Trang chủ</span>
        </Link>
        <Link href="/menu" aria-current={active === "menu" ? "page" : undefined}>
          <UtensilsCrossed />
          <span>Menu</span>
        </Link>
        <Link href="/orders" aria-current={active === "orders" ? "page" : undefined}>
          <Truck />
          <span>Đơn hàng</span>
        </Link>
        <Link href="/account" aria-current={active === "account" ? "page" : undefined}>
          <UserCircle />
          <span>Tài khoản</span>
        </Link>
        <Link href="/cart" aria-current={active === "cart" ? "page" : undefined}>
          <ShoppingBag />
          <span>Giỏ ({count})</span>
        </Link>
      </nav>
    </>
  );
}
