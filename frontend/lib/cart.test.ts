import { beforeEach, describe, expect, it, vi } from "vitest";
import { addToCart, readCart, writeCart } from "./cart";

describe("guest cart", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it("merges the same product with the same customization", () => {
    addToCart({ productId: "drink-1", name: "Trà sữa", price: 30_000, options: "Ít đá" });
    addToCart({ productId: "drink-1", name: "Trà sữa", price: 30_000, options: "Ít đá" });
    expect(readCart()).toEqual([{ productId: "drink-1", name: "Trà sữa", price: 30_000, options: "Ít đá", quantity: 2 }]);
  });

  it("keeps different customizations as separate lines", () => {
    addToCart({ productId: "drink-1", name: "Trà sữa", price: 30_000, options: "Ít đá" });
    addToCart({ productId: "drink-1", name: "Trà sữa", price: 35_000, options: "Nhiều đá, trân châu" });
    expect(readCart()).toHaveLength(2);
  });

  it("caps quantity and announces writes", () => {
    const dispatch = vi.spyOn(window, "dispatchEvent");
    writeCart([{ productId: "drink-1", name: "Trà sữa", price: 30_000, quantity: 20 }]);
    expect(() => addToCart({ productId: "drink-1", name: "Trà sữa", price: 30_000 })).toThrow("20 ly");
    expect(readCart()[0].quantity).toBe(20);
    expect(dispatch).toHaveBeenCalled();
  });
  it("discards damaged nested options and negative prices from storage", () => {
    localStorage.setItem("quedrink-guest-cart", JSON.stringify([
      { productId: "drink-1", name: "Trà sữa", price: 30000, quantity: 1, selection: { size: null, toppings: "bad" } },
      { productId: "drink-2", name: "Cà phê", price: -100, quantity: 1 },
      { productId: "drink-3", name: "Trà đào", price: 25000, quantity: 1 }
    ]));
    expect(readCart().map(item => item.productId)).toEqual(["drink-3"]);
  });
});
