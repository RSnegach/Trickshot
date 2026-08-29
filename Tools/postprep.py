"""Build Assets/Resources/Audio/post_hit.wav - the ball off the woodwork.

Download: https://kenney.nl/assets/impact-sounds  (CC0)
  -> Temp/kenney_impact-sounds.zip

Source pick. Of the 15 metal impacts in that pack, impactMetal_medium_000 has by far
the right modal profile for a goal frame: 71% of its ring energy sits in 100-1200 Hz
(the "light" set is 23-25%, i.e. tinny) and it rings for 0.27 s rather than dying in
0.11 s like most of the others. It is still a small pipe, not a goal.

Processing.
  1. VARI-SPEED DOWN. A regulation aluminium crossbar is a 7.3 m tube; its modes sit
     several hundred Hz below a hand-sized pipe's and it rings correspondingly longer.
     Resampling drops pitch and stretches time together, which is exactly the pair of
     changes wanted: the 838/995 Hz cluster lands near 380/450 Hz and the 0.27 s ring
     becomes 0.6 s. Doing this by pitch shift alone would give the frequency of a big
     bar with the decay of a small one, which reads as a synthesizer.
  2. BALL LAYER. You hear two things, not one: the bar ringing, and the dull slap of
     the ball itself. That slap is a ~80 Hz thump dead in a few tens of ms - compare
     ball_kick.wav, whose own strongest partials measure at 68/72/120 Hz. A damped
     sine under the clang supplies it. Without this the hit has no weight.
  3. ZERO LATENCY. Any leading silence would put the sound behind the visual contact,
     so the file starts on the transient.

Output: 44.1 kHz mono 16-bit. Mono because it plays on a 3D positional source, where
a stereo image is discarded anyway.
"""
import io, os, zipfile
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly, butter, sosfilt

SRC   = 'Temp/kenney_impact-sounds.zip'
PICK  = 'Audio/impactMetal_medium_000.ogg'
OUT   = 'Assets/Resources/Audio/post_hit.wav'
SR    = 44100
SLOW  = 2.2      # vari-speed factor (pitch down and lengthen together)
THUMP = 80.0     # Hz, the ball's own contribution
PEAK  = 0.89     # matches ball_kick.wav's 0.87 so the two sit at one level

x, sr = sf.read(io.BytesIO(zipfile.ZipFile(SRC).read(PICK)), dtype='float64', always_2d=True)
x = x.mean(axis=1)

# --- to the working rate, then vari-speed by resampling at a rate we then relabel ---
if sr != SR:
    x = resample_poly(x, SR, sr)
# 10x/22x is 2.2 exactly, and both factors are small enough for a clean polyphase filter.
x = resample_poly(x, 22, 10)

# --- start on the transient (5% of peak), keeping 1 ms of run-up so the attack is not clipped ---
env = np.convolve(np.abs(x), np.ones(int(SR * 0.002)) / (SR * 0.002), 'same')
onset = int(np.argmax(env > env.max() * 0.05))
x = x[max(0, onset - int(SR * 0.001)):]

# --- the ball's thump: damped sine, gone in ~35 ms, plus its octave for definition ---
t = np.arange(int(SR * 0.12)) / SR
thump = (np.sin(2 * np.pi * THUMP * t) * np.exp(-t / 0.035)
         + 0.35 * np.sin(2 * np.pi * THUMP * 2 * t) * np.exp(-t / 0.018))
y = x.copy()
y[:len(thump)] += thump * 0.55 * np.abs(x).max()

# --- 45 Hz high-pass (kills the resampler's DC drift and inaudible rumble) ---
y = sosfilt(butter(2, 45.0, 'highpass', fs=SR, output='sos'), y)

# --- edges: 1.5 ms in so there is no click, 25 ms out so the tail does not truncate ---
a, b = int(SR * 0.0015), int(SR * 0.025)
y[:a] *= np.linspace(0, 1, a)
y[-b:] *= np.linspace(1, 0, b)

y *= PEAK / max(np.abs(y).max(), 1e-9)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
sf.write(OUT, y.astype(np.float32), SR, subtype='PCM_16')
print('%s  %.3fs  %d Hz mono  peak %.2f' % (OUT, len(y) / SR, SR, np.abs(y).max()))
