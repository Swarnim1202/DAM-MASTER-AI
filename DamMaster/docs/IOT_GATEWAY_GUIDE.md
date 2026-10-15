# IoT gateway integration guide (Phase 4)

Gateways (data loggers with a modem) send readings by **MQTT** (preferred) or **HTTPS**. Both paths use the same
validation, duplicate protection and alert engine.

## 1. Register the gateway
**Gateways → Register gateway** (Admin / Instrumentation Engineer): code (e.g. `GW-DM001-01`), dam, expected message
interval. The response shows the MQTT topics and an HTTP API key (shown once; rotate any time).

## 2. MQTT topics (QoS 1)
| Direction | Topic | Content |
|---|---|---|
| Gateway → platform | `dammaster/{damCode}/{gatewayCode}/telemetry` | Readings (format below) |
| Gateway → platform | `dammaster/{damCode}/{gatewayCode}/status` | Health: battery, signal, buffer size, uptime, firmware |
| Platform → gateway | `dammaster/{damCode}/{gatewayCode}/ack` | Result per message: accepted / rejected / quarantined / duplicates / late, or `error` |

The topic root (`dammaster`) is `Mqtt:TopicRoot` in appsettings. The `damId` and `gatewayId` inside the payload must
match the topic, and the gateway must be registered to that dam, otherwise the message is refused (ack with `error`).

## 3. Telemetry payload
```json
{ "schemaVersion": "1.0", "messageId": "GW-DM001-01-000123", "gatewayId": "GW-DM001-01", "damId": "DM-001",
  "readings": [
    { "sensorId": "DM001-PZ-07", "parameterCode": "UPL", "observedAt": "2026-10-05T06:00:00+05:30", "value": 198.4, "unit": "kPa", "rawValue": 8235.6, "loggerStatus": "OK" }
  ] }
```
- `messageId` must be **unique per gateway** and **reused when the same message is re-sent**. A repeated `messageId`
  (QoS 1 redelivery, HTTP retry) is acknowledged with `duplicateMessage: true` and not processed again.
- `observedAt` is the **measurement time** with its offset. Readings without an offset are read as IST.
- Units must match the sensor's engineering unit; values are validated exactly as for Excel imports
  (range, logger status, spike, flatline, duplicates, quarantine for unknown sensors).

## 4. Buffering during outages
1. Keep readings on the logger while the link is down (store at least 7 days).
2. On reconnect, send the backlog oldest first, in messages of up to ~500 readings, each with its own `messageId`.
3. Wait for the `ack` of each message before deleting it from the logger buffer; if no ack arrives in 60 s, re-send the
   **same** message (same `messageId`).

The platform accepts late readings (any past time, never future), counts them as **late** in the message log, and the
gateway's **Reconciliation** view shows back-filled readings and gaps still open. "No data" sensor alerts and the
gateway-offline alert close themselves once data resumes.

## 5. Status message (every hour)
```json
{ "gatewayId": "GW-DM001-01", "batteryV": 12.6, "signalDbm": -71, "bufferedCount": 0, "uptimeS": 86400, "firmware": "2.3.1" }
```
Battery below `Gateway:LowBatteryVolts` (default 11.5 V) raises a Level 1 alert.

## 6. HTTPS alternative
`POST /api/telemetry` with headers `X-Gateway-Code` and `X-Api-Key`, same JSON body. Response 202 (processed) or
200 with `duplicateMessage: true`.

## 7. Broker set-up (Mosquitto example)
Production: TLS on 8883, one username per gateway, and an ACL that limits each gateway to its own topics.
```
# /etc/mosquitto/conf.d/dammaster.conf
listener 8883
cafile   /etc/mosquitto/certs/ca.crt
certfile /etc/mosquitto/certs/server.crt
keyfile  /etc/mosquitto/certs/server.key
allow_anonymous false
password_file /etc/mosquitto/passwd
acl_file /etc/mosquitto/acl
persistence true
```
```
# /etc/mosquitto/acl
user dammaster-api
topic read dammaster/+/+/telemetry
topic read dammaster/+/+/status
topic write dammaster/+/+/ack

user GW-DM001-01
topic write dammaster/DM-001/GW-DM001-01/telemetry
topic write dammaster/DM-001/GW-DM001-01/status
topic read  dammaster/DM-001/GW-DM001-01/ack
```
API settings: `Mqtt:Enabled=true`, `Host`, `Port=8883`, `UseTls=true`, `Username=dammaster-api`, `Password`.
The API uses a **persistent session** (clean session off, fixed `ClientId`), so the broker queues QoS 1 messages
while the API restarts. Run only one API instance per `ClientId`.
