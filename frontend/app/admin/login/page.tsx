"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { readAdminSession, writeAdminSession } from "../../../lib/admin-auth";
import { Coffee, ShieldCheck, Mail, Lock, ArrowLeft, Loader2 } from "lucide-react";

const api = process.env.NEXT_PUBLIC_API_URL || "/api/v1";
type LoginResponse = {
  success: boolean;
  data: {
    accessToken: string;
    refreshToken: string;
    expiresIn: number;
    user: { displayName: string; email: string | null; roles: string[] };
  };
  message?: string;
};

export default function AdminLoginPage() {
  const router = useRouter();
  const [error, setError] = useState("");
  const [sending, setSending] = useState(false);

  useEffect(() => {
    if (readAdminSession()) router.replace("/admin");
  }, [router]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const form = new FormData(event.currentTarget);
    setSending(true);
    setError("");
    try {
      const response = await fetch(`${api}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ identity: form.get("identity"), password: form.get("password") })
      });
      const payload = (await response.json()) as LoginResponse;
      if (!response.ok) throw new Error(payload.message || "Không thể đăng nhập. Vui lòng kiểm tra lại thông tin.");
      if (!payload.data.user.roles.includes("ADMIN")) throw new Error("Tài khoản này không có quyền truy cập quản trị.");
      
      writeAdminSession({
        accessToken: payload.data.accessToken,
        refreshToken: payload.data.refreshToken,
        expiresAt: Date.now() + payload.data.expiresIn * 1000,
        displayName: payload.data.user.displayName,
        email: payload.data.user.email
      });
      router.replace("/admin");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Không thể đăng nhập. Vui lòng thử lại.");
    } finally {
      setSending(false);
    }
  }

  return (
    <main className="min-h-screen bg-slate-900 flex flex-col justify-center items-center p-6 relative overflow-hidden">
      {/* Dynamic ambient glow */}
      <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-[#154d3d]/30 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-orange-600/20 rounded-full blur-3xl pointer-events-none" />

      <div className="w-full max-w-md bg-white/95 backdrop-blur-xl p-8 rounded-3xl border border-white/20 shadow-2xl relative z-10 space-y-6">
        <div className="text-center space-y-2">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-[#154d3d] text-white shadow-lg mb-2">
            <Coffee className="w-8 h-8" />
          </div>
          <span className="text-xs font-bold text-emerald-800 uppercase tracking-widest block">Quản Trị Hệ Thống</span>
          <h1 className="text-2xl font-black text-slate-900">QueDrink Admin Portal</h1>
          <p className="text-xs text-slate-500">Đăng nhập tài khoản nội bộ để quản lý đơn hàng & doanh thu.</p>
        </div>

        {error && (
          <div className="p-4 rounded-xl bg-red-50 border border-red-100 text-red-700 text-xs font-semibold">
            ⚠️ {error}
          </div>
        )}

        <form onSubmit={submit} className="space-y-4">
          <div className="space-y-1">
            <label htmlFor="admin-identity" className="text-xs font-bold text-slate-700 block">Email hoặc số điện thoại</label>
            <div className="relative">
              <Mail className="w-4 h-4 text-slate-400 absolute left-3.5 top-3.5" />
              <input
                name="identity"
                id="admin-identity"
                required
                maxLength={255}
                autoComplete="username"
                placeholder="admin@quedrink.vn"
                className="w-full pl-10 pr-4 py-3 rounded-xl border border-slate-200 text-sm focus:border-[#154d3d] focus:outline-none"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label htmlFor="admin-password" className="text-xs font-bold text-slate-700 block">Mật khẩu bảo mật</label>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-400 absolute left-3.5 top-3.5" />
              <input
                name="password"
                id="admin-password"
                type="password"
                required
                maxLength={72}
                autoComplete="current-password"
                placeholder="••••••••"
                className="w-full pl-10 pr-4 py-3 rounded-xl border border-slate-200 text-sm focus:border-[#154d3d] focus:outline-none"
              />
            </div>
          </div>

          <button
            className="button button--primary w-full py-3 mt-2 shadow-lg shadow-emerald-900/30 flex items-center justify-center gap-2"
            disabled={sending}
          >
            {sending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" /> Đang xác thực...
              </>
            ) : (
              <>
                <ShieldCheck className="w-4 h-4" /> Đăng nhập hệ thống
              </>
            )}
          </button>
        </form>

        <div className="text-center pt-2 border-t border-slate-100">
          <Link href="/menu" className="inline-flex items-center gap-1 text-xs font-bold text-slate-500 hover:text-[#154d3d]">
            <ArrowLeft className="w-3.5 h-3.5" /> Quay lại trang chọn món
          </Link>
        </div>
      </div>
    </main>
  );
}
