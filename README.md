<div align="center">
  <img src="Gafelsound.png" alt="GafelSound Logo" width="220" height="220" />
  <h1>GafelSound</h1>
  <p><b>Modern Multi-Output Audio Manager for Windows</b></p>
  <p>Play system audio simultaneously across multiple playback devices (e.g., Desktop Speakers + Bluetooth Headphones) with independent volume control and a clean, modern UI.</p>

  <p>
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Platform" />
    <img src="https://img.shields.io/badge/.NET-11.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 11" />
    <img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?style=for-the-badge&logo=python&logoColor=white" alt="Python" />
    <img src="https://img.shields.io/badge/Audio%20Engine-WASAPI%20Loopback-239C72?style=for-the-badge" alt="Audio Engine" />
    <img src="https://img.shields.io/badge/Release-v1.0.0-397AF5?style=for-the-badge" alt="Release" />
  </p>
</div>

---

## ✨ Features

* **Simultaneous Multi-Output Playback:** Duplicate and stream Windows system audio to multiple output devices at the exact same time (Speakers, Bluetooth Headsets, HDMI Monitors, USB DACs) without needing virtual audio cables.
* **Independent Per-Device Volume & Mute:** Adjust volume sliders or mute each connected output device independently in real time.
* **Live Windows Audio Session Detection:** Automatically scans and displays active applications currently playing audio with quick access to Windows per-app volume routing.
* **Low-Latency WASAPI Engine:** Real-time WASAPI Loopback capture with automatic sample rate and channel resampling, plus configurable buffer sizes (`256`, `512`, `1024` samples).
* **Modern Light & Dark UI:** Clean, responsive interface built with HTML/CSS/JS running inside a native desktop WebView window.
* **Two Editions Available:** Choose between the native **C# / .NET 11** edition (recommended for lower memory footprint & installer support) or the original **Python** edition.

---
## 🖼️ GafelSound Preview

<div align="center">
  <img src="App.png" alt="GafelSound Preview" width="550" />
</div>

---
## 🚀 Quick Start (Installer / Portable `.exe`)

1. Go to the [**Releases**](../../releases) section of this repository.
2. Download either **`GafelSound-Setup-v1.0.0.exe`** (Installer) or the portable **`GafelSound.exe`**.
3. Launch **GafelSound** — no virtual audio drivers required.
4. Toggle **Playback enabled** on any connected audio devices in the **Dashboard** to start simultaneous playback.

---

## 🛠️ Running from Source

First, clone the repository:

```bash
git clone https://github.com/White-Wolf-dv/GafelSound.git
cd GafelSound
```

### Option 1: .NET 11 Edition (Recommended)

#### Prerequisites
* Windows 10 or Windows 11
* [.NET 11 SDK](https://dotnet.microsoft.com/)
* Microsoft Edge WebView2 Runtime (pre-installed on Windows 10/11)

#### Run
```bash
cd DotNet
dotnet run
```

#### Build Portable `.exe` & Windows Installer
```bash
cd DotNet
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
To build the Windows Setup executable (`GafelSound-Setup-v1.0.0.exe`), open `DotNet/setup.iss` in **Inno Setup** and compile it (`F9`).

---

### Option 2: Python Edition

#### Prerequisites
* Windows 10 or Windows 11
* Python 3.10 or higher

#### Install & Run
```bash
cd Python
pip install pywebview pyaudiowpatch pycaw comtypes psutil numpy pillow
python app.py
```

#### Build Standalone `.exe` (PyInstaller)
```bash
cd Python
pip install pyinstaller
pyinstaller --noconsole --onefile --clean --name "GafelSound" --icon="Gafelsound.ico" --add-data "ui.html;." --add-data "Gafelsound.ico;." --add-data "Gafelsound.png;." --hidden-import="pycaw" --hidden-import="comtypes" --hidden-import="pyaudiowpatch" app.py
```
The compiled executable will be generated in the `Python/dist/` directory.

---

## 📁 Project Structure

```text
GafelSound/
├── DotNet/                 # C# (.NET 11) + WinForms + WebView2 + NAudio edition
│   ├── GafelSound.csproj   # Project configuration
│   ├── Program.cs          # Application entry point
│   ├── MainForm.cs         # WebView2 host & JS bridge
│   ├── MultiAudioEngine.cs # WASAPI loopback & multi-output audio engine
│   ├── Models.cs           # Data models & settings
│   ├── ui.html             # Frontend UI
│   ├── Gafelsound.ico      # Application icon
│   └── setup.iss           # Inno Setup installer script
├── Python/                 # Original Python + PyWebView + PyAudioWPatch edition
│   ├── app.py              # WASAPI multi-output audio engine & PyWebView backend
│   ├── ui.html             # Frontend dashboard, routing, devices & settings UI
│   └── Gafelsound.ico      # Windows executable & window icon
├── Gafelsound.png          # Application logo (PNG)
├── App.png                 # Application preview screenshot
├── LICENSE                 # License file
└── README.md               # Project documentation
```

---

<br>
<div align="center">
  <img src="https://github.com/White-Wolf-dv/White-Wolf-dv/blob/main/Gif/57.gif" alt="GafelSound Preview" width="550" />
</div>

---

## 📄 License & Copyright

This project is licensed under the **PolyForm Noncommercial License 1.0.0**, with additional attribution requirements.

- Free for personal and non-commercial use.
- Modification and redistribution are permitted for non-commercial purposes.
- Commercial use, sale, or monetization is prohibited without prior written permission from the original author.
- Proper credit to the original author must be preserved in any project using this software or any part of it.

**Original Author:** [White-Wolf-dv](https://github.com/White-Wolf-dv)

**Original Repository:** [https://github.com/White-Wolf-dv/GafelSound](https://github.com/White-Wolf-dv/GafelSound)

See the [LICENSE](./LICENSE) file for the complete license terms and additional attribution requirements.

© 2026 White-Wolf-dv. All rights reserved.
