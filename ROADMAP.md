# 🗺️ Bluetooth Battery Monitor Roadmap

This document outlines the strategic vision, upcoming milestones, and community-driven priorities for **Bluetooth Battery Monitor**. We welcome community input, feedback, and pull requests on any of the planned roadmap items!

---

## 🎯 Project Vision

To be the definitive, native, and privacy-focused battery management utility for Windows 10 & 11 — delivering instant, lightweight, and hardware-rich telemetry for every connected Bluetooth, BLE, and 2.4 GHz wireless peripheral without heavyweight vendor bloatware.

---

## 📍 Milestones

### 🟢 Version 1.0.0 — Native Fluent Foundation (Current Release)

- [x] **Windows 11 Fluent Design Flyout:**
  - Modern borderless window with native Mica backdrop material and rounded corners.
  - Multi-monitor taskbar alignment (`TaskbarPositionHelper`).
  - Smooth opacity fade transitions and smart click-outside dismissal.
- [x] **Dynamic GDI+ System Tray Icon:**
  - Real-time lowest battery level calculation directly drawn on the tray icon.
  - Tri-color status indicators: 🟢 Healthy (40–100%), 🟡 Warning (20–39%), 🔴 Critical (<20%).
  - ⚡ Charging bolt overlay badge for plugged-in devices.
- [x] **Modular 11-Provider Telemetry Engine:**
  - 36 hardware protocol families decoded (BLE GATT, Apple AirPods Beacon, Windows PnP, Logitech HID++, Logitech G HUB, DualShock 4 / DualSense PS5, Samsung Galaxy Buds RFCOMM, Sony MDR RFCOMM, Google Fast Pair, Nintendo Switch Joy-Con / Pro, SteelSeries Arctis/Nova).
- [x] **TWS Earbuds Channel Monitoring:**
  - Independent Left Earbud, Right Earbud, and Charging Case levels.
- [x] **Smart Audio Routing & Codec Inspector:**
  - Auto-routing Windows default audio endpoint on device connection.
  - Real-time audio codec detection: LDAC, aptX HD, aptX, AAC, SBC.
  - Interactive volume slider and mute controls per device card.
- [x] **Local REST API Server:**
  - Embedded zero-dependency HTTP server (`http://127.0.0.1:23253/devices`) with CORS.
- [x] **Automated Real Screenshot Pipeline:**
  - `--capture-screenshots` CLI command rendering authentic WPF windows to `docs/screenshots/`.
- [x] **Test Coverage:**
  - 253 passing xUnit unit and integration tests.

---

### 🟡 Version 1.1.0 — Analytics & Ecosystem (Q4 2026)

- [ ] **Battery Discharge Analytics & Time Remaining:**
  - Track historical discharge rate (% per hour).
  - Accurate time remaining estimator based on moving average usage.
- [ ] **Interactive Battery History Charts:**
  - Micro sparkline charts on device cards showing battery drop over the last 24 hours.
- [ ] **Additional Hardware Providers:**
  - [ ] Bose QuietComfort (QC35, QC45, 700) RFCOMM SPP protocol.
  - [ ] JBL & Harman Kardon portable speaker protocol.
  - [ ] Marshall Bluetooth speaker and headphone telemetry.
  - [ ] Razer wireless mice and headsets (Hyperspeed 2.4GHz / BLE).
- [ ] **Multi-Language UI (i18n):**
  - Turkish, English, German, Japanese, Spanish, and Simplified Chinese localization.
- [ ] **GitHub Releases Auto-Updater:**
  - Non-intrusive notification when a new release is available on GitHub with one-click download.

---

### 🟠 Version 1.2.0 — Windows 11 Widgets & Home Automation (Q1 2027)

- [ ] **Windows 11 Widget Board Provider:**
  - Native Windows 11 adaptive card widget showing battery percentages in the Windows Widget board (`Win + W`).
- [ ] **Home Automation & Smart Home Webhooks:**
  - Configurable Webhook triggers on low battery (send notification to Home Assistant, Discord, or Telegram).
  - MQTT broker publisher for smart home integration.
- [ ] **Stream Deck & Rainmeter First-Class Plugins:**
  - Official Stream Deck key action displaying live battery gauge and charging state.
  - Sample Rainmeter skin leveraging the local REST API.
- [ ] **Custom Audio Alerts:**
  - Play subtle sound chimes on low battery warnings or disconnection.

---

### 🟣 Version 2.0.0 — Next-Gen Cross-Platform Telemetry (Long-Term)

- [ ] **Companion Mobile Sync (Android BLE Bridge):**
  - Track your smartphone's battery directly in the Windows taskbar over a secure local BLE advertisement without cloud servers.
- [ ] **Multi-PC Peer Synchronization:**
  - Sync battery levels across desktop and laptop over local Wi-Fi / mDNS.
- [ ] **Dynamic Plugin Architecture:**
  - Allow community developers to write and distribute custom battery provider DLLs without recompiling the core application.

---

## 💬 Have a Feature Request?

Join the discussion! If you'd like to suggest an idea, request a device protocol, or help build any of the features above, please open a [Feature Request Issue](https://github.com/Kaandonmez/BluetoothBatteryMonitor/issues/new?template=feature_request.yml) or start a discussion on GitHub.
