export type CartItem = {
  productId: string;
  name: string;
  price: number;
  quantity: number;
  categoryName?: string;
  imageUrl?: string | null;
  options?: string;
  selection?: CartSelection;
};

export type SelectedOption = { id: string; name: string };
export type SelectedPricedOption = SelectedOption & { price: number };
export type CartSelection = {
  size: SelectedPricedOption;
  sugar: SelectedOption;
  ice: SelectedOption;
  toppings: SelectedPricedOption[];
  note?: string;
};

const KEY = "quedrink-guest-cart";
const validOption = (value: unknown, priced = false): boolean => {
  if (!value || typeof value !== "object") return false;
  const option = value as SelectedPricedOption;
  return typeof option.id === "string" && /^[0-9a-f-]{36}$/i.test(option.id) && typeof option.name === "string" && option.name.length <= 100 && (!priced || Number.isSafeInteger(option.price) && option.price >= 0);
};

export function readCart(): CartItem[] {
  if (typeof window === "undefined") return [];
  try {
    const value: unknown = JSON.parse(localStorage.getItem(KEY) || "[]");
    if (!Array.isArray(value)) return [];
    return value.filter((item): item is CartItem => Boolean(item && typeof item === "object" &&
      typeof (item as CartItem).productId === "string" && typeof (item as CartItem).name === "string" &&
      Number.isSafeInteger((item as CartItem).price) && (item as CartItem).price >= 0 && Number.isInteger((item as CartItem).quantity) &&
      (item as CartItem).quantity >= 1 && (item as CartItem).quantity <= 20 &&
      (!(item as CartItem).selection || (() => {
        const selection = (item as CartItem).selection!;
        return validOption(selection.size, true) && validOption(selection.sugar) && validOption(selection.ice) && Array.isArray(selection.toppings) && selection.toppings.length <= 5 && selection.toppings.every(t => validOption(t, true)) &&
          (selection.note === undefined || typeof selection.note === "string" && selection.note.length <= 300);
      })()))).slice(0, 30);
  } catch {
    return [];
  }
}

export function writeCart(items: CartItem[]) {
  localStorage.setItem(KEY, JSON.stringify(items));
  window.dispatchEvent(new Event("quedrink-cart"));
}

export function addToCart(item: Omit<CartItem, "quantity">) {
  const items = readCart();
  const keyMatch = (i: CartItem) => cartKey(i) === cartKey(item);
  const found = items.find(keyMatch);

  if (found) {
    if (found.quantity >= 20) throw new Error("Mỗi lựa chọn tối đa 20 ly. Bạn có thể đặt riêng nếu cần thêm.");
    found.quantity++;
  } else {
    if (items.length >= 30) throw new Error("Giỏ đã có 30 lựa chọn. Vui lòng hoàn tất đơn hiện tại trước.");
    items.push({ ...item, quantity: 1 });
  }
  writeCart(items);
}

export function cartKey(item: Omit<CartItem, "quantity"> | CartItem) {
  if (!item.selection) return `${item.productId}|${item.options || ""}`;
  const toppingIds = item.selection.toppings.map((topping) => topping.id).sort().join(",");
  return [item.productId, item.selection.size.id, item.selection.sugar.id, item.selection.ice.id,
    toppingIds, item.selection.note?.trim() || ""].join("|");
}

export function describeSelection(item: CartItem) {
  if (!item.selection) return item.options || "";
  return [item.selection.size.name, item.selection.sugar.name, item.selection.ice.name,
    ...item.selection.toppings.map((topping) => topping.name), item.selection.note ? `Ghi chú: ${item.selection.note}` : ""]
    .filter(Boolean).join(", ");
}
