import os
import sys
import json
import time
import winreg
import threading
import numpy as np
import webview
import pyaudiowpatch as pyaudio
from ctypes import cast, POINTER
from comtypes import CLSCTX_ALL, CoInitialize, CoUninitialize
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

CONFIG_FILE = "gafelsound_config.json"


class MultiAudioEngine:

    def __init__(self):
        self.p = pyaudio.PyAudio()
        self.running = False
        self.devices = {}  # id -> device metadata & state
        self.output_streams = {}  # id -> PyAudio stream
        self.lock = threading.Lock()
        self.buffer_size = 512
        self.independent_volume = True
        self.capture_thread = None

        self.settings = {
            "theme": "light",
            "start_with_windows": False,
            "minimize_to_tray": True,
            "start_minimized": False,
            "buffer_size": 512,
        }
        self.load_config()
        self.scan_devices()

    def load_config(self):
        if os.path.exists(CONFIG_FILE):
            try:
                with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    self.settings.update(data.get("settings", {}))
                    self.buffer_size = int(self.settings.get("buffer_size", 512))
            except Exception:
                pass

    def save_config(self):
        try:
            with open(CONFIG_FILE, "w", encoding="utf-8") as f:
                json.dump({"settings": self.settings}, f, indent=2)
        except Exception:
            pass

    def _classify_device_type(self, name):
        n = name.lower()
        if "bluetooth" in n or "hands-free" in n or "airpods" in n or "headset" in n:
            return "Bluetooth", "♧"
        if "headphone" in n:
            return "Headphones", "♧"
        if "hdmi" in n or "nvidia" in n or "amd" in n or "display" in n or "monitor" in n:
            return "HDMI", "▣"
        return "Speakers", "◖))"

    def scan_devices(self):
        """شناسایی تمام کارت‌های صدای خروجی فعال در ویندوز از طریق WASAPI"""
        with self.lock:
            old_states = {
                k: {
                    "enabled": v["enabled"],
                    "volume": v["volume"],
                    "muted": v["muted"],
                }
                for k, v in self.devices.items()
            }
            self.devices.clear()

            try:
                wasapi_info = self.p.get_host_api_info_by_type(pyaudio.paWASAPI)
                default_out_idx = wasapi_info.get("defaultOutputDevice", -1)
                host_index = wasapi_info["index"]
            except OSError:
                return []

            for i in range(self.p.get_device_count()):
                dev = self.p.get_device_info_by_index(i)
                if (
                    dev.get("hostApi") == host_index
                    and dev.get("maxOutputChannels", 0) > 0
                    and not dev.get("isLoopbackDevice", False)
                ):
                    dev_id = str(i)
                    name = dev.get("name", f"Device {i}")
                    is_default = i == default_out_idx
                    dtype, icon = self._classify_device_type(name)
                    sr = int(dev.get("defaultSampleRate", 48000))
                    ch = min(2, int(dev.get("maxOutputChannels", 2)))

                    prev = old_states.get(
                        dev_id,
                        {
                            "enabled": is_default,
                            "volume": 75,
                            "muted": False,
                        },
                    )

                    self.devices[dev_id] = {
                        "id": dev_id,
                        "index": i,
                        "name": name,
                        "desc": f"WASAPI · {dtype} Endpoint",
                        "type": dtype,
                        "icon": icon,
                        "status": "Connected",
                        "is_default": is_default,
                        "sample_rate": sr,
                        "channels": ch,
                        "format": f"{sr // 1000} kHz · {'Stereo' if ch == 2 else 'Mono'}",
                        "volume": prev["volume"],
                        "muted": prev["muted"],
                        "enabled": prev["enabled"],
                    }
            return list(self.devices.values())

    def start_engine(self):
        if self.running:
            return
        self.running = True
        self.capture_thread = threading.Thread(
            target=self._audio_loop, daemon=True
        )
        self.capture_thread.start()

    def stop_engine(self):
        self.running = False
        if self.capture_thread and self.capture_thread.is_alive():
            self.capture_thread.join(timeout=1.5)
        self._close_all_outputs()

    def restart_engine(self):
        self.stop_engine()
        self.start_engine()

    def _close_all_outputs(self):
        for s in list(self.output_streams.values()):
            try:
                s.stop_stream()
                s.close()
            except Exception:
                pass
        self.output_streams.clear()

    def _audio_loop(self):
        """حلقه اصلی ضبط صدای ویندوز (Loopback) و پخش همزمان در چند خروجی"""
        CoInitialize()
        loopback_stream = None
        try:
            wasapi_info = self.p.get_host_api_info_by_type(pyaudio.paWASAPI)
            default_Index = wasapi_info["defaultOutputDevice"]
            default_speaker = self.p.get_device_info_by_index(default_Index)

            # پیدا کردن Loopback مربوط به خروجی پیش‌فرض ویندوز
            loopback_dev = None
            for loopback in self.p.get_loopback_device_info_generator():
                if default_speaker["name"] in loopback["name"]:
                    loopback_dev = loopback
                    break

            if not loopback_dev:
                self.running = False
                return

            in_sr = int(loopback_dev["defaultSampleRate"])
            in_ch = int(loopback_dev["maxInputChannels"])

            loopback_stream = self.p.open(
                format=pyaudio.paFloat32,
                channels=in_ch,
                rate=in_sr,
                input=True,
                input_device_index=loopback_dev["index"],
                frames_per_buffer=self.buffer_size,
            )

            while self.running:
                try:
                    raw_data = loopback_stream.read(
                        self.buffer_size, exception_on_overflow=False
                    )
                except Exception:
                    time.sleep(0.01)
                    continue

                audio_chunk = np.frombuffer(raw_data, dtype=np.float32)
                if len(audio_chunk) == 0:
                    continue

                with self.lock:
                    for dev_id, dev in self.devices.items():
                        # خروجی پیش‌فرض خودش صدا را پخش می‌کند؛ فقط به خروجی‌های کمکیِ فعال ارسال می‌کنیم
                        if dev["is_default"]:
                            continue

                        if not dev["enabled"]:
                            if dev_id in self.output_streams:
                                try:
                                    self.output_streams[dev_id].stop_stream()
                                    self.output_streams[dev_id].close()
                                except Exception:
                                    pass
                                del self.output_streams[dev_id]
                            continue

                        # باز کردن استریم برای خروجی ثانویه (مثلاً هدست بلوتوثی یا مانیتور)
                        if dev_id not in self.output_streams:
                            try:
                                self.output_streams[dev_id] = self.p.open(
                                    format=pyaudio.paFloat32,
                                    channels=dev["channels"],
                                    rate=dev["sample_rate"],
                                    output=True,
                                    output_device_index=dev["index"],
                                    frames_per_buffer=self.buffer_size,
                                )
                            except Exception:
                                continue

                        # اعمال حجم صدا و Mute مستقل برای هر خروجی
                        vol_factor = (
                            0.0
                            if dev["muted"]
                            else (dev["volume"] / 100.0)
                        )
                        processed = audio_chunk * vol_factor

                        # تطبیق تعداد کانال‌ها و نرخ نمونه‌برداری در صورت تفاوت
                        frames = len(processed) // in_ch
                        reshaped = processed.reshape((frames, in_ch))

                        if dev["channels"] < in_ch:
                            reshaped = reshaped[:, : dev["channels"]]
                        elif dev["channels"] > in_ch:
                            reshaped = np.tile(reshaped, (1, dev["channels"]))

                        if dev["sample_rate"] != in_sr and frames > 1:
                            target_frames = int(
                                frames * dev["sample_rate"] / in_sr
                            )
                            x_old = np.linspace(0, 1, frames)
                            x_new = np.linspace(0, 1, target_frames)
                            resampled = np.zeros(
                                (target_frames, dev["channels"]),
                                dtype=np.float32,
                            )
                            for c in range(dev["channels"]):
                                resampled[:, c] = np.interp(
                                    x_new, x_old, reshaped[:, c]
                                )
                            reshaped = resampled

                        try:
                            self.output_streams[dev_id].write(
                                reshaped.astype(np.float32).tobytes()
                            )
                        except Exception:
                            try:
                                self.output_streams[dev_id].close()
                            except Exception:
                                pass
                            del self.output_streams[dev_id]

        except Exception as e:
            print("Engine error:", e)
        finally:
            if loopback_stream:
                try:
                    loopback_stream.stop_stream()
                    loopback_stream.close()
                except Exception:
                    pass
            self._close_all_outputs()
            CoUninitialize()

    def set_device_volume(self, dev_id, volume):
        dev_id = str(dev_id)
        vol = max(0, min(100, int(volume)))
        with self.lock:
            if dev_id in self.devices:
                self.devices[dev_id]["volume"] = vol
                # اگر دستگاه پیش‌فرض است، صدای مستر ویندوز را هم تنظیم کن
                if self.devices[dev_id]["is_default"]:
                    self._set_windows_default_volume(vol / 100.0, None)
        return True

    def set_device_mute(self, dev_id, muted):
        dev_id = str(dev_id)
        with self.lock:
            if dev_id in self.devices:
                self.devices[dev_id]["muted"] = bool(muted)
                if self.devices[dev_id]["is_default"]:
                    self._set_windows_default_volume(None, bool(muted))
        return True

    def set_device_enabled(self, dev_id, enabled):
        dev_id = str(dev_id)
        with self.lock:
            if dev_id in self.devices:
                self.devices[dev_id]["enabled"] = bool(enabled)
                if self.devices[dev_id]["is_default"]:
                    self._set_windows_default_volume(None, not bool(enabled))
        return True

    def _set_windows_default_volume(self, scalar_vol=None, mute=None):
        try:
            CoInitialize()
            speakers = AudioUtilities.GetSpeakers()
            interface = speakers.Activate(
                IAudioEndpointVolume._iid_, CLSCTX_ALL, None
            )
            volume = cast(interface, POINTER(IAudioEndpointVolume))
            if scalar_vol is not None:
                volume.SetMasterVolumeLevelScalar(float(scalar_vol), None)
            if mute is not None:
                volume.SetMute(1 if mute else 0, None)
        except Exception:
            pass
        finally:
            CoUninitialize()

    def get_active_audio_apps(self):
        """دریافت لیست برنامه‌های در حال پخش صدا در ویندوز"""
        apps = []
        seen = set()
        try:
            CoInitialize()
            sessions = AudioUtilities.GetAllSessions()
            for s in sessions:
                if s.Process and s.Process.name():
                    pname = s.Process.name()
                    pid = s.Process.pid
                    if pname not in seen:
                        seen.add(pname)
                        icon = "🌐" if any(b in pname.lower() for b in ["chrome", "msedge", "firefox", "brave"]) else (
                               "♫" if any(m in pname.lower() for m in ["spotify", "music", "vlc", "wmplayer"]) else (
                               "💬" if any(c in pname.lower() for c in ["discord", "telegram", "teams", "zoom"]) else "▶"))
                        apps.append({
                            "pid": pid,
                            "name": pname.replace(".exe", "").capitalize(),
                            "process": pname,
                            "icon": icon,
                            "status": "Application audio · Active Session"
                        })
        except Exception:
            pass
        finally:
            CoUninitialize()
        return apps

    def set_startup_registry(self, enable):
        key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
        app_name = "GafelSoundAudioManager"
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE
            )
            if enable:
                exe_path = f'"{sys.executable}" "{os.path.abspath(__file__)}"'
                winreg.SetValueEx(key, app_name, 0, winreg.REG_SZ, exe_path)
            else:
                try:
                    winreg.DeleteValue(key, app_name)
                except FileNotFoundError:
                    pass
            winreg.CloseKey(key)
        except Exception:
            pass


class WebAppAPI:
    """پل ارتباطی بین رابط گرافیکی جاوااسکریپت و موتور پایتون"""

    def __init__(self, engine: MultiAudioEngine):
        self.engine = engine

    def get_initial_state(self):
        return {
            "devices": list(self.engine.devices.values()),
            "apps": self.engine.get_active_audio_apps(),
            "settings": self.engine.settings,
            "engine_running": self.engine.running,
        }

    def refresh_devices(self):
        devs = self.engine.scan_devices()
        return devs

    def refresh_apps(self):
        return self.engine.get_active_audio_apps()

    def set_volume(self, dev_id, volume):
        self.engine.set_device_volume(dev_id, volume)
        return {"ok": True}

    def toggle_mute(self, dev_id, muted):
        self.engine.set_device_mute(dev_id, muted)
        return {"ok": True}

    def toggle_enable(self, dev_id, enabled):
        self.engine.set_device_enabled(dev_id, enabled)
        return {"ok": True}

    def update_setting(self, key, value):
        self.engine.settings[key] = value
        if key == "buffer_size":
            self.engine.buffer_size = int(value) if str(value).isdigit() else 512
            self.engine.restart_engine()
        elif key == "start_with_windows":
            self.engine.set_startup_registry(bool(value))
        self.engine.save_config()
        return {"ok": True}

    def open_windows_mixer(self):
        """باز کردن تنظیمات پیشرفته Routing ویندوز در صورت نیاز"""
        os.system("start ms-settings:apps-volume")
        return {"ok": True}
    
def resource_path(relative_path):
    """دریافت مسیر دقیق فایل‌ها در حالت اجرا و حالت کامپایل‌شده PyInstaller"""
    if hasattr(sys, '_MEIPASS'):
        return os.path.join(sys._MEIPASS, relative_path)
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), relative_path)

if __name__ == "__main__":
    engine = MultiAudioEngine()
    engine.start_engine()

    api = WebAppAPI(engine)
    html_path = resource_path("ui.html")
    icon_path = resource_path("Gafelsound.ico")  # یا Gafelsound.png

    if not os.path.exists(icon_path):
        icon_path = None

    window = webview.create_window(
        title="GafelSound",
        url=html_path,
        js_api=api,
        width=1240,
        height=820,
        min_size=(640, 520)
    )

    try:
        webview.start(icon=icon_path, debug=False)
    finally:
        engine.stop_engine()