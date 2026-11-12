"use client";
import Image from "next/image";
import { useState } from "react";

export function DrinkVisual({ categoryName, name, compact = false, imageUrl, priority = false }: { categoryName?: string; name: string; compact?: boolean; imageUrl?: string | null; priority?: boolean }) {
  const [failed, setFailed] = useState(false);
  const fallback = name.toLowerCase().includes("matcha") ? "matcha" : categoryName === "Cà phê" ? "coffee" : categoryName === "Trà hoa quả" || categoryName === "Nước ép" ? "fruit" : categoryName === "Sinh tố" ? "milk-tea" : "boba";
  const custom = !failed && imageUrl && (imageUrl.startsWith("/images/") || imageUrl.startsWith("https://"));
  return <div className={`drink-visual drink-photo${compact ? " drink-visual--compact" : ""}`}>
    <Image src={custom ? imageUrl! : `/images/${fallback}.jpg`} alt={`${name}${custom ? "" : " — ảnh minh họa"}`} fill sizes={compact ? "104px" : "(max-width: 600px) 50vw, (max-width: 1000px) 50vw, 400px"} loading={priority ? "eager" : "lazy"} unoptimized={Boolean(custom)} onError={() => setFailed(true)} />
    {!compact && <span className="drink-visual__caption">{custom ? categoryName : "Ảnh minh họa"}</span>}
  </div>;
}
