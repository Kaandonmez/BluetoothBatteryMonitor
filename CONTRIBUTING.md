# Contributing to Bluetooth Battery Monitor

First off, thank you for considering contributing to **Bluetooth Battery Monitor**! 🎉
Whether you're fixing a bug, adding support for new Bluetooth hardware, refining the Fluent Design UI, or improving documentation, your help makes this project better for Windows users worldwide.

---

## 🧭 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
  - [Reporting Bugs](#reporting-bugs)
  - [Requesting New Device Support](#requesting-new-device-support)
  - [Suggesting Enhancements](#suggesting-enhancements)
  - [Submitting Pull Requests](#submitting-pull-requests)
- [Development Environment Setup](#development-environment-setup)
  - [Prerequisites](#prerequisites)
  - [Building and Running](#building-and-running)
  - [Running Unit Tests](#running-unit-tests)
  - [Regenerating Authentic UI Screenshots](#regenerating-authentic-ui-screenshots)
- [Architecture & Adding a New Battery Provider](#architecture--adding-a-new-battery-provider)
- [Coding Guidelines & Commit Conventions](#coding-guidelines--commit-conventions)

---

## 📜 Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior via GitHub Issues or contact the maintainers.

---

## 💡 How Can I Contribute?

### 🐛 Reporting Bugs

Before creating a bug report, please check existing [Issues](https://github.com/Kaandonmez/BluetoothBatteryMonitor/issues) to avoid duplicates.

When reporting a bug, use the **[Bug Report Template](https://github.com/Kaandonmez/BluetoothBatteryMonitor/issues/new?template=bug_report.yml)** and provide:
- Your Windows version and build (e.g. Windows 11 23H2 build 22631).
- Device make, model, and connection type (BLE, Classic Bluetooth, 2.4 GHz Dongle).
- Clear, reproducible steps to trigger the bug.
- Expected behavior vs. actual behavior.
- Relevant error logs or console messages.

### 🎧 Requesting New Device Support

Have a Bluetooth headset, TWS earbuds, mouse, keyboard, or controller whose battery isn't being detected?
We welcome device requests! Please use the **[New Device Support Template](https://github.com/Kaandonmez/BluetoothBatteryMonitor/issues/new?template=device_support.yml)** with:
- Device Model & Manufacturer (e.g. Anker Soundcore Liberty 4 NC).
- Hardware / PnP Device ID from Windows Device Manager (`BTHENUM\...` or `HID\...`).
- Bluetooth MAC address vendor prefix (OUI).
- Known companion app protocols (if reverse-engineered).

### 🚀 Submitting Pull Requests

1. **Fork the repository** on GitHub.
2. **Clone your fork** locally:
   ```powershell
   git clone https://github.com/<your-username>/BluetoothBatteryMonitor.git
   cd BluetoothBatteryMonitor
   ```
3. **Create a topic branch** from `main`:
   ```powershell
   git checkout -b feature/add-bose-qc-support
   ```
4. **Implement your changes** following the coding standards.
5. **Run all tests** to ensure no regressions:
   ```powershell
   dotnet test
   ```
6. **Commit your changes** using [Conventional Commits](#commit-conventions).
7. **Push to your fork** and open a Pull Request against `main`.

---

## 🛠️ Development Environment Setup

### Prerequisites

- **Windows 10 (Build 19041+) or Windows 11** (Required for WinRT Bluetooth APIs).
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (v8.0.100 or later).
- **IDE**: [Visual Studio 2022](https://visualstudio.microsoft.com/) (v17.8+ with *.NET Desktop Development* workload) or [Visual Studio Code](https://code.visualstudio.com/) with C# Dev Kit.

### Building and Running

Clone and build the solution:
```powershell
# Restore and build the solution
dotnet build

# Run the Fluent WPF Application in development mode
dotnet run --project src/BluetoothBatteryMonitor.App/BluetoothBatteryMonitor.App.csproj
```

### Running Unit Tests

The test suite includes 253+ unit and integration tests covering protocol parsers, aggregator deduplication, audio routing, REST API, and lifecycle management:

```powershell
dotnet test
```

To run a specific test class:
```powershell
dotnet test --filter "FullyQualifiedName~BluetoothBatteryEngine"
```

### 📸 Regenerating Authentic UI Screenshots

To keep repository screenshots 100% genuine and reproducible without AI mockups, the application includes an automated screenshot engine:

```powershell
dotnet run --project src/BluetoothBatteryMonitor.App/BluetoothBatteryMonitor.App.csproj -- --capture-screenshots
```

This runs the real application windows (Flyout, Settings, About) with sample hardware models and saves clean high-resolution assets to `docs/screenshots/`.

---

## 🏗️ Architecture & Adding a New Battery Provider

All hardware battery decoders implement the clean `IBluetoothBatteryProvider` interface:

```csharp
public interface IBluetoothBatteryProvider : IDisposable
{
    string ProviderName { get; }
    event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    void StartMonitoring();
    void StopMonitoring();
    Task<IEnumerable<BluetoothDeviceModel>> ScanDevicesAsync();
}
```

### Step-by-Step: Adding a New Device Provider

1. **Create the provider class** in `src/BluetoothBatteryMonitor.App/Services/Bluetooth/YourBrandBatteryProvider.cs`:
   ```csharp
   public class BoseQuietComfortBatteryProvider : IBluetoothBatteryProvider
   {
       public string ProviderName => "Bose QuietComfort SPP";
       public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

       public void StartMonitoring() { /* Start BLE advertisement watcher or RFCOMM listener */ }
       public void StopMonitoring() { /* Cleanup timers/sockets */ }
       public async Task<IEnumerable<BluetoothDeviceModel>> ScanDevicesAsync()
       {
           // Return discovered devices
           return devices;
       }
       public void Dispose() { /* ... */ }
   }
   ```

2. **Register the provider** in `BluetoothBatteryEngine.cs`:
   ```csharp
   _providers.Add(new BoseQuietComfortBatteryProvider());
   ```

3. **Add unit tests** in `tests/BluetoothBatteryMonitor.Tests/YourBrandTests.cs`:
   - Mock raw telemetry packets/bytes.
   - Verify battery percentage, charging state, and TWS channel extraction.
   - Ensure `dotnet test` passes.

---

## 🎨 Coding Guidelines & Commit Conventions

### C# Style
- Use **file-scoped namespaces** (`namespace BluetoothBatteryMonitor.App;`).
- Enable **nullable reference types** (`#nullable enable`).
- Use **MVVM** pattern with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- Follow Windows Fluent Design guidelines for any UI modifications (WPF-UI library).

### Commit Conventions

We follow [Conventional Commits](https://www.conventionalcommits.org/):

- `feat: add support for Bose QuietComfort 45 RFCOMM telemetry`
- `fix: correct case charging status on AirPods Pro 2 BLE packet`
- `docs: update roadmap with widget integration milestone`
- `test: add unit tests for Samsung Buds SOM byte decoder`
- `refactor: optimize MAC address deduplication lookup table`
- `chore: update WPF-UI dependency to v4.3.1`

---

## ⭐ Star the Repo!

If you find Bluetooth Battery Monitor helpful, please consider giving the repository a **Star** on GitHub! It helps other Windows users discover the project and motivates ongoing open-source maintenance.
