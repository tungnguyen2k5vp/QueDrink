"""Isolated local VNPAY contract check. Does not contact VNPAY or transfer money.
Run only with your own local fixture key: QUE_VNPAY_TEST_SECRET, QUE_ADMIN_PASSWORD.
"""
import concurrent.futures
import hashlib
import hmac
import json
import os
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("QUE_API", "http://127.0.0.1:5087/api/v1")
SECRET = os.environ["QUE_VNPAY_TEST_SECRET"]
MERCHANT = os.environ.get("QUE_VNPAY_TEST_MERCHANT", "FIXTURE1")


def call(path, data=None, token=None, key=None, code=200):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if key:
        headers["Idempotency-Key"] = key
    request = urllib.request.Request(BASE + path, None if data is None else json.dumps(data).encode(), headers)
    try:
        response = urllib.request.urlopen(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    body = json.loads(response.read())
    assert response.status == code, (path, response.status, body.get("message"))
    return body.get("data", body)


def sign(fields):
    data = urllib.parse.urlencode(sorted((k, v) for k, v in fields.items() if v != "" and k not in ("vnp_SecureHash", "vnp_SecureHashType")), safe="!()*-._")
    return hmac.new(SECRET.encode(), data.encode(), hashlib.sha512).hexdigest()


def result(intent, **changes):
    fields = {"vnp_TmnCode": MERCHANT, "vnp_TxnRef": intent["reference"], "vnp_Amount": str(intent["amount"] * 100),
              "vnp_ResponseCode": "00", "vnp_TransactionStatus": "00", "vnp_TransactionNo": str(uuid.uuid4().int)[:18]}
    fields.update(changes)
    fields["vnp_SecureHash"] = sign(fields)
    return fields


def ipn(fields, suffix=""):
    return call("/payments/vnpay/ipn?" + urllib.parse.urlencode(fields) + suffix)["RspCode"]


store = call("/store")
assert store["paymentProvider"] == "VNPAY" and store["paymentMode"] == "SANDBOX", "Use an isolated sandbox fixture server"
admin = call("/auth/login", {"identity": "admin@quedrink.local", "password": os.environ["QUE_ADMIN_PASSWORD"]})["accessToken"]
assert not call("/admin/integrations", token=admin)["gmailConfigured"], "Disable real Gmail during tests"
call("/admin/integrations/gmail/test", {}, token=admin, code=409)
p = call("/products")[0]
detail = call("/products/" + p["slug"])
payload = {"name": "VNPAY fixture", "phone": "0912345678", "deliveryType": "PICKUP", "paymentMethod": "BANK_TRANSFER",
           "items": [{"productId": p["id"], "sizeId": detail["sizes"][0]["id"], "quantity": 1}]}


def new_order():
    order = call("/orders", payload, key=uuid.uuid4().hex, code=201)
    key = uuid.uuid4().hex
    intent = call("/payments/intents", {"orderCode": order["orderCode"], "phone": payload["phone"]}, key=key, code=201)
    replay = call("/payments/intents", {"orderCode": order["orderCode"], "phone": payload["phone"]}, key=key)
    assert replay["reference"] == intent["reference"]
    fields = dict(urllib.parse.parse_qsl(urllib.parse.urlsplit(intent["checkoutUrl"]).query))
    assert urllib.parse.urlsplit(intent["checkoutUrl"]).hostname == "sandbox.vnpayment.vn"
    assert fields["vnp_SecureHash"] == sign(fields) and fields["vnp_Amount"] == str(order["total"] * 100)
    return order, intent


def lookup(order):
    return call("/orders/lookup", {"orderCode": order["orderCode"], "phone": payload["phone"]})


order, intent = new_order()
fields = result(intent)
assert ipn({**fields, "vnp_SecureHash": "0" * 128}) == "97"
assert ipn(result(intent, vnp_Amount=str(intent["amount"] * 100 + 1))) == "04"
assert ipn(result(intent, vnp_TmnCode="WRONG123")) == "97"
assert ipn(fields, "&vnp_Amount=1") == "97"
assert lookup(order)["paymentStatus"] == "UNPAID"
# Browser return may redirect but must never credit the order.
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None

try:
    urllib.request.build_opener(NoRedirect).open(BASE + "/payments/vnpay/return?" + urllib.parse.urlencode(fields), timeout=10)
except urllib.error.HTTPError as redirect:
    assert redirect.code == 302 and order["orderCode"] in redirect.headers["Location"]
assert lookup(order)["paymentStatus"] == "UNPAID"
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    replies = list(pool.map(ipn, [fields, fields]))
assert sorted(replies) == ["00", "02"]
paid = lookup(order)
assert paid["paymentStatus"] == "PAID" and paid["orderStatus"] == "CONFIRMED" and paid["version"] == order["version"] + 1
assert ipn(result(intent, vnp_ResponseCode="24", vnp_TransactionStatus="02")) == "02"
assert lookup(order)["paymentStatus"] == "PAID"
call("/admin/payments/" + order["orderCode"] + "/mock", {}, token=admin, code=404)
call("/payments/callbacks/mock", {"reference": intent["reference"], "orderCode": order["orderCode"], "amount": intent["amount"], "status": "SUCCESS"}, code=404)

cancelled, late = new_order()
call("/orders/cancel", {"orderCode": cancelled["orderCode"], "phone": payload["phone"]})
assert ipn(result(late)) == "00"
review = lookup(cancelled)
assert review["orderStatus"] == "CANCELLED" and review["paymentStatus"] == "REVIEW"

double, first = new_order()
assert ipn(result(first, vnp_ResponseCode="24", vnp_TransactionStatus="02")) == "00"
second = call("/payments/intents", {"orderCode": double["orderCode"], "phone": payload["phone"]}, key=uuid.uuid4().hex, code=201)
assert second["reference"] != first["reference"]
assert ipn(result(first)) == "00"
assert ipn(result(second)) == "00"
assert lookup(double)["paymentStatus"] == "PAID"
reviews = call("/admin/payments/review", token=admin)
assert any(p["reference"] == late["reference"] for p in reviews)
assert any(p["reference"] == second["reference"] for p in reviews)
print("PASS: VNPAY URL/signature/amount/merchant, IPN replay/concurrency, browser return, mock isolation, late and duplicate charge review. No external payment was made.")
