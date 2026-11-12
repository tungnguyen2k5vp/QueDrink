"""Run against an isolated Development database. Uses only Python's standard library.
QUE_API=http://127.0.0.1:5086/api/v1 QUE_ADMIN_PASSWORD=... QUE_PAYMENT_SECRET=... python check_api.py
Creates uniquely named test fixtures; never run against customer data.
"""
import concurrent.futures
import hashlib
import hmac
import json
import os
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get("QUE_API", "http://127.0.0.1:5086/api/v1")
TAG = uuid.uuid4().hex[:10]
PASSWORD = "LocalTest!2026-" + TAG


def call(path, data=None, *, method=None, token=None, key=None, expect=200, headers=None):
    head = {"Content-Type": "application/json", **(headers or {})}
    if token:
        head["Authorization"] = "Bearer " + token
    if key:
        head["Idempotency-Key"] = key
    request = urllib.request.Request(BASE + path, None if data is None else json.dumps(data).encode(), head,
                                     method=method or ("POST" if data is not None else "GET"))
    try:
        response = urllib.request.urlopen(request, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read()
    body = json.loads(raw) if raw else {}
    assert response.status == expect, (path, response.status, body.get("message"))
    return body.get("data")


admin = call("/auth/login", {"identity": os.environ.get("QUE_ADMIN_EMAIL", "admin@quedrink.local"),
                              "password": os.environ["QUE_ADMIN_PASSWORD"]})["accessToken"]
session = call("/auth/register", {"displayName": "API self-check", "email": TAG + "@example.test",
                                  "password": PASSWORD}, expect=201)
user = session["accessToken"]
assert session["user"]["roles"] == ["CUSTOMER"]
call("/admin/orders", token=user, expect=403)
call("/admin/orders", expect=401)
products = call("/products")
detail = call("/products/" + products[0]["slug"])
p = detail["product"]
item = {"productId": p["id"], "sizeId": detail["sizes"][0]["id"], "sugarOptionId": detail["sugarOptions"][0]["id"],
        "iceOptionId": detail["iceOptions"][0]["id"], "toppingIds": [], "quantity": 2, "price": 1}
quote = call("/orders/quote", {"items": [item], "deliveryType": "PICKUP"})
assert quote["subtotal"] == 2 * ((p["salePrice"] if p["salePrice"] is not None else p["basePrice"]) + detail["sizes"][0]["price"])
assert quote["shippingFee"] == 0
call("/orders/quote", {"items": [{**item, "sizeId": str(uuid.uuid4())}]}, expect=409)
call("/orders/quote", {"items": [{**item, "quantity": 0}]}, expect=400)
call("/orders/quote", {"items": [None]}, expect=400)
payload = {"name": "Test customer", "phone": "0912345678", "deliveryType": "PICKUP", "paymentMethod": "COD", "items": [item]}
key = uuid.uuid4().hex
order = call("/orders", payload, token=user, key=key, expect=201)
replay = call("/orders", payload, token=user, key=key)
assert order["orderCode"] == replay["orderCode"]
call("/orders", {**payload, "name": "Changed name"}, token=user, key=key, expect=409)
call("/orders", payload, key=key, expect=409)
call("/orders", payload, expect=400)
call("/orders/lookup", {"orderCode": order["orderCode"], "phone": "0987654321"}, expect=404)
assert any(o["id"] == order["id"] for o in call("/orders/mine", token=user))
call("/me/favorites/" + p["id"], {}, token=user, expect=201)
call("/me/favorites/" + p["id"], {}, token=user, expect=201)
assert call("/me/favorites", token=user).count(p["id"]) == 1
call("/me/reviews", {"orderId": order["id"], "productId": p["id"], "rating": 5}, token=user, expect=403)
call("/admin/orders/" + order["orderCode"] + "/status", {"status": "COMPLETED"}, method="PATCH", token=admin, expect=409)
for status in ["CONFIRMED", "PREPARING", "READY", "COMPLETED"]:
    assert status in order["allowedStatuses"]
    order = call("/admin/orders/" + order["orderCode"] + "/status", {"status": status, "expectedVersion": order["version"]}, method="PATCH", token=admin)
assert order["paymentStatus"] == "PAID" and not order["allowedStatuses"]
assert call("/me/loyalty", token=user)["points"] == order["total"] // 10000
review = {"orderId": order["id"], "productId": p["id"], "rating": 5, "comment": "API check"}
call("/me/reviews", review, token=user, expect=201)
call("/me/reviews", review, token=user, expect=409)

voucher = {"code": "CHECK_" + TAG.upper(), "discountType": "FIXED", "discountValue": 1000, "usageLimit": 1}
call("/admin/vouchers", voucher, token=admin, expect=201)
def consume(_):
    try:
        return call("/orders", {**payload, "voucherCode": voucher["code"]}, key=uuid.uuid4().hex, expect=201)
    except AssertionError as error:
        assert error.args[0][1] == 409, error
        return None
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    attempts = list(pool.map(consume, range(2)))
assert sum(o is not None for o in attempts) == 1, "Voucher oversold under concurrency"
reserved = next(o for o in attempts if o)
call("/orders/cancel", {"orderCode": reserved["orderCode"], "phone": payload["phone"]})
call("/orders/quote", {"items": [item], "voucherCode": voucher["code"]})

bank = call("/orders", {**payload, "paymentMethod": "BANK_TRANSFER"}, key=uuid.uuid4().hex, expect=201)
call("/admin/orders/" + bank["orderCode"] + "/status", {"status": "CONFIRMED"}, method="PATCH", token=admin, expect=409)
intent = call("/payments/intents", {"orderCode": bank["orderCode"], "phone": payload["phone"]}, key=uuid.uuid4().hex, expect=201)
assert intent["isDemo"] and intent["provider"] == "MOCK"
callback = {"reference": intent["reference"], "orderCode": bank["orderCode"], "amount": intent["amount"], "status": "SUCCESS"}
call("/payments/callbacks/mock", callback, expect=403)
secret = os.environ["QUE_PAYMENT_SECRET"]
def signed(data, expect=200):
    message = f'{data["reference"]}|{data["orderCode"]}|{data["amount"]}|{data["status"]}'
    signature = hmac.new(secret.encode(), message.encode(), hashlib.sha256).hexdigest()
    return call("/payments/callbacks/mock", data, headers={"X-Payment-Signature": signature}, expect=expect)
signed({**callback, "amount": intent["amount"] + 1}, expect=400)
assert signed(callback)["paymentStatus"] == "PAID"
assert signed(callback)["paymentStatus"] == "PAID"
assert signed({**callback, "status": "FAILED"})["status"] == "SUCCESS"
call("/orders/cancel", {"orderCode": bank["orderCode"], "phone": payload["phone"]}, expect=409)

category = call("/categories")[0]
created = call("/admin/products", {"name": "Check " + TAG, "categoryId": category["id"], "basePrice": 25000,
                                    "sizes": [{"name": "M", "price": 0}]}, token=admin, expect=201)
assert call("/products/" + created["slug"])["sizes"][0]["price"] == 0
old_size_id = call("/products/" + created["slug"])["sizes"][0]["id"]
call("/admin/products/" + created["id"], {"sizes": [{"name": "M", "price": 1000}, {"name": "L", "price": 5000}]}, token=admin, method="PATCH")
new_sizes = call("/products/" + created["slug"])["sizes"]
assert new_sizes[0]["id"] == old_size_id and new_sizes[0]["price"] == 1000
call("/admin/products/" + created["id"], {"sizes": [None]}, token=admin, method="PATCH", expect=400)
call("/admin/products/" + created["id"], method="DELETE", token=admin, expect=204)
call("/products/" + created["slug"], expect=404)

new_session = call("/auth/refresh", {"refreshToken": session["refreshToken"]})
call("/auth/refresh", {"refreshToken": session["refreshToken"]}, expect=401)
call("/orders/mine", token=user, expect=401)
call("/auth/logout", {"refreshToken": new_session["refreshToken"]})
call("/orders/mine", token=new_session["accessToken"], expect=401)
print("PASS: auth/RBAC/rotation/logout, authoritative pricing, options, idempotency, voucher concurrency/cancel, states, loyalty/reviews, payment signatures/replay, catalog validation.")
