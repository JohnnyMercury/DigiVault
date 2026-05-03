# Overpay deployment notes

Overpay (`api-pay.overpay.io`) requires **mTLS** — every outbound request from
our server must present a client certificate during the TLS handshake. The
`.p12` file is **never committed to git**. It must be placed manually on the
production host before the integration is enabled.

## One-time host setup

```bash
# 1. Create a secrets directory outside any web-root.
sudo mkdir -p /var/www/digivault/secrets

# 2. Copy the .p12 file delivered by Overpay support to the host
#    (e.g. via scp from your laptop).
scp ./cert.p12 user@key-zona.com:/tmp/overpay.p12
ssh user@key-zona.com 'sudo mv /tmp/overpay.p12 /var/www/digivault/secrets/overpay.p12'

# 3. Lock down permissions - only root and the docker user should read it.
sudo chmod 600 /var/www/digivault/secrets/overpay.p12
sudo chown root:root /var/www/digivault/secrets/overpay.p12
```

`docker-compose.prod.yml` mounts `/var/www/digivault/secrets` into the
container read-only at the same path, so the named HttpClient configured in
`Program.cs` finds the file at `/var/www/digivault/secrets/overpay.p12`.

## Configure credentials

The `PaymentProviderConfig{Name=overpay}` row is seeded by `DbSeeder` with
the sandbox credentials:

| Field          | Value                                           |
| -------------- | ----------------------------------------------- |
| `ApiKey`       | HTTP Basic username (sandbox: `key-zona`)       |
| `SecretKey`    | HTTP Basic password (sandbox: `ISGCF?\|YNtdzPH1`) |
| `MerchantId`   | `projectId` (sandbox: `1084`)                   |
| `Settings`     | `{ certPath, certPass, baseUrl }`               |
| `IsEnabled`    | `false` (until you've placed the .p12)          |
| `IsTestMode`   | `true` for sandbox, set to `false` for prod     |

Edit them via **`/Admin/PaymentProviders`** (the page lets you change the
JSON `Settings` blob too) and flip **Enabled** when ready.

## Webhook URL

In the Overpay LK (`https://lk.overpay.io/`) set the order-update webhook to:

```
https://key-zona.com/api/webhooks/overpay
```

The body Overpay sends is `{ id, status, merchantTransactionId }` with **no
signature**. We protect against spoofed webhooks by re-fetching the order
status from `GET /orders/{id}` over the same mTLS channel — an attacker who
doesn't have our `.p12` can't forge a successful round-trip there.

## Smoke test (sandbox)

After enabling and putting cert in place:

1. Open `/Catalog/Steam` (or any product page) → step-2 picker → **Overpay** tile.
2. Pay with sandbox card `4111 1111 1111 1145` (frictionless) or
   `4111 1111 1111 1111` + OTP `111111` (3DS).
3. Watch logs: `docker logs digivault-web -f --tail 200 | grep Overpay`.
4. Order should flip to `Processing` → `Completed` after webhook + verify.
