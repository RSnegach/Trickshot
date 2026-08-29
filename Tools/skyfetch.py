import os, sys, subprocess
import numpy as np
from PIL import Image

OUT = 'Temp/nature/sky'
os.makedirs(OUT, exist_ok=True)

CANDIDATES = [
    'kloppenheim_06_puresky',
    'kloppenheim_02_puresky',
    'mud_road_puresky',
    'pizzo_pernice_puresky',
    'kloofendal_38d_partly_cloudy_puresky',
    'qwantani_mid_morning_puresky',
    'autumn_field_puresky',
    'mpumalanga_veld_puresky',
    'farm_field_puresky',
]
# Already in the project; measured as the baseline to beat.
EXISTING = {
    'qwantani_noon_puresky (CURRENT MENU)': 'Assets/Resources/Sky/qwantani_noon_puresky.jpg',
    'kloofendal_48d_partly_cloudy_puresky': 'Assets/Resources/Sky/kloofendal_48d_partly_cloudy_puresky.jpg',
}

def fetch(name):
    dst = os.path.join(OUT, name + '.jpg')
    if os.path.exists(dst) and os.path.getsize(dst) > 50000:
        return dst
    url = 'https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/' + name + '.jpg'
    r = subprocess.run(['curl', '--ssl-no-revoke', '-sSL', '--max-time', '120', '-o', dst, url],
                       capture_output=True)
    if os.path.exists(dst) and os.path.getsize(dst) > 50000:
        return dst
    return None

# The band the menu camera can actually see. Camera pitches 15 deg DOWN with a 46 deg vertical lens,
# so the top of frame is at -15 + 23 = +8 deg elevation and the horizon is at 0. In an equirectangular
# panorama, elevation e maps to row H * (0.5 - e/180), so this is only the 4.4% of rows just above the
# middle of the image. Everything below the horizon is stadium and turf, not sky.
TOP_ELEV = 8.0
BOT_ELEV = 0.0

def measure(path):
    im = Image.open(path).convert('RGB')
    a = np.asarray(im, dtype=np.float32) / 255.0
    H = a.shape[0]
    r0 = int(H * (0.5 - TOP_ELEV / 180.0))
    r1 = int(H * (0.5 - BOT_ELEV / 180.0))
    band = a[r0:r1]
    lum = 0.2126 * band[:, :, 0] + 0.7152 * band[:, :, 1] + 0.0722 * band[:, :, 2]
    # Detail: std across the band, which is what "has cloud in it" looks like numerically. The project
    # already used this measure (see SkyDome's comment: 0.069 kloofendal vs 0.028 qwantani_noon).
    mean_rgb = band.reshape(-1, 3).mean(axis=0)
    # warmth: how far red leads blue. Positive = warm/orange, negative = cool/blue.
    warmth = float(mean_rgb[0] - mean_rgb[2])
    return dict(rows=(r0, r1), lum=float(lum.mean()), lo=float(lum.min()), hi=float(lum.max()),
                std=float(lum.std()), rgb=mean_rgb, warmth=warmth, size=im.size)

rows = []
for label, path in EXISTING.items():
    if os.path.exists(path):
        rows.append((label, measure(path), 'in project'))
for name in CANDIDATES:
    p = fetch(name)
    if p is None:
        print('  FAILED to fetch ' + name)
        continue
    rows.append((name, measure(p), '%.0f KB' % (os.path.getsize(p) / 1024.0)))

print()
print('Visible band = elevation 0 to +8 deg (rows just above image centre)')
print('%-40s %6s %6s %6s %7s   %-22s %s' % ('sky', 'lum', 'min', 'max', 'detail', 'mean rgb', 'note'))
for label, m, note in rows:
    print('%-40s %6.3f %6.3f %6.3f %7.3f   (%.2f,%.2f,%.2f)  %s'
          % (label, m['lum'], m['lo'], m['hi'], m['std'],
             m['rgb'][0], m['rgb'][1], m['rgb'][2], note))

print()
print('Ranked by DETAIL (cloud structure in the visible band), brightest-first tiebreak:')
for label, m, note in sorted(rows, key=lambda r: (-r[1]['std'], -r[1]['lum'])):
    warm = 'warm' if m['warmth'] > 0.02 else ('cool' if m['warmth'] < -0.02 else 'neutral')
    print('   %-40s detail=%.3f lum=%.3f %s' % (label, m['std'], m['lum'], warm))
