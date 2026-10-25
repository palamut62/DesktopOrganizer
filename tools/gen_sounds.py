"""Cekmece sesleri, malzemeye gore sentezlenir (lisans/indirme yok). Kullanim:
python tools/gen_sounds.py  ->  src/Dolap.App/Sounds/{metal,blocks}-{open,close}.wav
Yalniz surtunme/kayma sesi; tik, mandal ve carpma sesi yok. Metal: bilyali ray kaymasi. Blok: plastik surtunme."""
import os
import wave
import numpy as np
from scipy.signal import butter, sosfilt

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Dolap.App", "Sounds")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


def t(sec):
    return np.arange(int(SR * sec)) / SR


def band(x, lo, hi, order=4):
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


def low(x, f, order=4):
    return sosfilt(butter(order, f, btype="low", fs=SR, output="sos"), x)


def modes(freqs, decays, amps, dur):
    """Vurulan cisim: soonumlu sinusler (modal sentez)."""
    tt = t(dur)
    return sum(a * np.exp(-tt / d) * np.sin(2 * np.pi * f * tt + rng.uniform(0, 6)) for f, d, a in zip(freqs, decays, amps))


def place(buf, sig, at):
    i = int(at * SR)
    n = min(len(sig), len(buf) - i)
    buf[i:i + n] += sig[:n]


def rail(dur, speed_from, speed_to, lo=900, hi=5200):
    """Bilyali ray: hiza gore sikligi degisen mikro tikirtilar + metalik hisirti."""
    tt = t(dur)
    rate = np.linspace(speed_from, speed_to, len(tt))
    phase = np.cumsum(rate) / SR
    ticks = (np.diff(np.floor(phase), prepend=0) > 0).astype(float) * rng.uniform(0.4, 1.0, len(tt))
    ticks = low(band(ticks, 1500, 5000, 2), 2500) * 0.6   # bilye tikirtisi yumusak, tik gibi duyulmaz
    hiss = band(rng.normal(size=len(tt)), lo, hi) * 0.45
    env = np.sin(np.pi * np.clip(tt / dur, 0, 1)) ** 0.6
    return (ticks + hiss) * env


def thunk(low_f, ring, dur=0.35, gain=1.0):
    tt = t(dur)
    body = np.sin(2 * np.pi * low_f * tt * (1 - 0.15 * tt)) * np.exp(-tt / 0.045)
    hit = band(rng.normal(size=len(tt)), 300, 3000) * np.exp(-tt / 0.006) * 0.8
    return gain * (body + hit + ring)


def friction(dur, lo, hi, rough=38):
    """Plastik surtunme: dar bant gurultu, takilip kayma (stick-slip) titresimi."""
    tt = t(dur)
    n = band(rng.normal(size=len(tt)), lo, hi)
    stick = 0.6 + 0.4 * np.abs(np.sin(2 * np.pi * rough * tt + 3 * np.sin(2 * np.pi * 3 * tt)))
    env = np.sin(np.pi * np.clip(tt / dur, 0, 1)) ** 0.8
    return n * stick * env * 0.5


def click(f1, f2, dur=0.06, gain=1.0):
    tt = t(dur)
    c = modes([f1, f2, f2 * 1.53], [0.006, 0.012, 0.004], [1.0, 0.6, 0.3], dur)
    snap = band(rng.normal(size=len(tt)), 2500, 9000) * np.exp(-tt / 0.0015) * 1.2
    return gain * (c + snap)


def write(name, x):
    x = x - np.mean(x)
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.7          # tepe -3 dBFS civari; sistem sesini bastirmaz
    fade = np.ones_like(x); k = int(0.01 * SR); fade[-k:] = np.linspace(1, 0, k)
    data = (x * fade * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())


def metal_open():
    buf = np.zeros(int(SR * 0.40))
    place(buf, rail(0.36, 120, 40), 0.0)
    write("metal-open.wav", low(buf, 7000))


def metal_close():
    buf = np.zeros(int(SR * 0.32))
    place(buf, rail(0.30, 60, 150), 0.0)
    write("metal-close.wav", low(buf, 7000))


def blocks_open():
    buf = np.zeros(int(SR * 0.30))
    place(buf, friction(0.28, 700, 3200), 0.0)
    write("blocks-open.wav", buf)


def blocks_close():
    buf = np.zeros(int(SR * 0.25))
    place(buf, friction(0.23, 800, 3600, rough=46), 0.0)
    write("blocks-close.wav", buf)


if __name__ == "__main__":
    metal_open(); metal_close(); blocks_open(); blocks_close()
    print("ok ->", os.path.abspath(OUT))
