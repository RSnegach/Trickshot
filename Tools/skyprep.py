"""
Turn the Poly Haven .hdr pureskies into game-ready equirectangular skybox textures,
and measure where each one's sun actually is.

Two reasons this happens here instead of at runtime:

  1. The project renders in GAMMA colour space with the Built-in pipeline and no
     post-processing stack, so there is nothing downstream that could use HDR range:
     values above 1 would simply clip. Tone-mapping now, with a curve I control, is
     strictly better looking than letting the hardware clamp - and it drops 106 MB of
     source to about a tenth of that.

  2. Unity's .hdr importer picks its own encoding per platform (BC6H, RGBM, half) and
     the shader has to decode whichever it chose. An 8-bit texture has exactly one
     interpretation, so the skybox shader stays trivial and cannot be wrong.

The sun measurement is the important half. With a real sky photo the sun is IN the
image, so the scene's directional light has to be aimed at the same spot or every
shadow on the pitch points somewhere the sky says it shouldn't.
"""

import io, math, os, struct, sys
import numpy as np
from PIL import Image

# Drop the 4K .hdr files from https://polyhaven.com/hdris (CC0) into SKY, then run this
# from the project root. Each .hdr is replaced by a .jpg and deleted.
SKY = 'Assets/Resources/Sky'
OUT_W, OUT_H = 4096, 2048


# ------------------------------------------------------------------ radiance .hdr
def read_hdr(path):
    """Decode a Radiance RGBE file to a float32 HxWx3 array of linear radiance."""
    f = io.open(path, 'rb')
    if not f.readline().startswith(b'#?'):
        raise ValueError('not a radiance file: ' + path)
    while True:
        line = f.readline()
        if line in (b'\n', b'\r\n', b''):
            break
    dims = f.readline().split()
    if len(dims) != 4 or dims[0] != b'-Y' or dims[2] != b'+X':
        raise ValueError('unsupported orientation: ' + repr(dims))
    h, w = int(dims[1]), int(dims[3])

    data = np.frombuffer(f.read(), dtype=np.uint8)
    f.close()
    rgbe = np.empty((h, w, 4), dtype=np.uint8)
    p = 0
    for y in range(h):
        # New-style RLE scanline: 0x02 0x02 <width hi> <width lo>, then four
        # separately run-length-encoded component planes.
        if (p + 4 <= data.size and data[p] == 2 and data[p + 1] == 2
                and ((int(data[p + 2]) << 8) | int(data[p + 3])) == w):
            p += 4
            for c in range(4):
                x = 0
                while x < w:
                    n = int(data[p]); p += 1
                    if n > 128:                      # a run of one repeated byte
                        rgbe[y, x:x + n - 128, c] = data[p]; p += 1
                        x += n - 128
                    else:                            # n literal bytes
                        rgbe[y, x:x + n, c] = data[p:p + n]; p += n
                        x += n
        else:
            # Flat scanline. (Old-style cross-scanline RLE is not produced by any
            # Poly Haven export, so a wrong-looking image here means a new source.)
            rgbe[y] = data[p:p + w * 4].reshape(w, 4)
            p += w * 4

    e = rgbe[:, :, 3].astype(np.int32)
    scale = np.where(e == 0, 0.0, np.ldexp(1.0, e - 136)).astype(np.float32)
    return rgbe[:, :, :3].astype(np.float32) * scale[:, :, None]


# ------------------------------------------------------------------ equirect maths
# Matches Shaders/SkyPanoramic.shader exactly: latitude runs from 0 at the zenith
# (image row 0) to pi at the nadir, and u = 0.5 - longitude / 2pi with
# longitude = atan2(z, -x).
def dir_from_pixel(col, row, w, h):
    lat = (row + 0.5) / h * math.pi
    lon = (0.5 - (col + 0.5) / w) * 2.0 * math.pi
    s = math.sin(lat)
    return (-s * math.cos(lon), math.cos(lat), s * math.sin(lon))


def lum(a):
    return a[:, :, 0] * 0.2126 + a[:, :, 1] * 0.7152 + a[:, :, 2] * 0.0722


def find_sun(a):
    """Luminance-weighted centroid of the brightest blob, as a world direction."""
    h, w = a.shape[:2]
    y = lum(a)
    # Coarse pass on a block mean so a single hot pixel of sensor noise cannot win.
    bh, bw = h // 64, w // 64
    coarse = y[:bh * 64, :bw * 64].reshape(bh, 64, bw, 64).mean(axis=(1, 3))
    cy, cx = np.unravel_index(np.argmax(coarse), coarse.shape)
    r0, r1 = max(0, (cy - 1) * 64), min(h, (cy + 2) * 64)
    c0, c1 = max(0, (cx - 1) * 64), min(w, (cx + 2) * 64)
    win = y[r0:r1, c0:c1]
    # Centroid over the top of the window only, so the surrounding glow does not
    # drag the centre away from the disc.
    thr = win.max() * 0.5
    m = np.where(win >= thr, win, 0.0)
    rows = np.arange(r0, r1)[:, None] + 0.5
    cols = np.arange(c0, c1)[None, :] + 0.5
    tot = m.sum()
    row = float((m * rows).sum() / tot)
    col = float((m * cols).sum() / tot)
    # Mean colour of the disc, normalised to its brightest channel: the tint the
    # directional light should carry.
    disc = a[r0:r1, c0:c1][win >= thr]
    tint = disc.mean(axis=0)
    tint = tint / max(tint.max(), 1e-6)
    return dir_from_pixel(col - 0.5, row - 0.5, w, h), tint


def sun_euler(d):
    """Euler for a directional light whose forward is -d (i.e. shining from d)."""
    n = math.sqrt(sum(c * c for c in d))
    x, y, z = (c / n for c in d)
    pitch = math.degrees(math.asin(max(-1.0, min(1.0, y))))
    yaw = math.degrees(math.atan2(-x, -z))
    return pitch, yaw


# ------------------------------------------------------------------ tone map
def tonemap(a):
    """Exposure-normalise, roll the highlights off, then gamma-encode to bytes."""
    y = lum(a)
    # Key off a high percentile of the SKY rather than the mean: the sun is orders
    # of magnitude brighter than everything else and would crush the exposure to
    # nothing, while the mean is dragged down by a dark lower hemisphere.
    ref = np.percentile(y[y > 0], 97.0)
    x = a * (1.45 / max(ref, 1e-6))
    x = 1.0 - np.exp(-np.maximum(x, 0.0))     # clean asymptote, no hard clip on the sun
    x = np.power(x, 1.0 / 2.2)
    # Dither before quantising. Unity compresses a 4K sky to DXT1, and DXT1 turns
    # smooth gradients into visible steps; a sub-LSB of noise breaks up the blocks
    # and costs nothing on a JPEG this size.
    rng = np.random.default_rng(20260824)
    x = x * 255.0 + rng.uniform(-0.6, 0.6, size=x.shape)
    return np.clip(x, 0, 255).astype(np.uint8)


def main():
    names = sorted(n for n in os.listdir(SKY) if n.endswith('.hdr'))
    for n in names:
        path = os.path.join(SKY, n)
        a = read_hdr(path)
        d, tint = find_sun(a)
        pitch, yaw = sun_euler(d)

        # Average of the upper hemisphere, tone-mapped: what the camera should clear
        # to and what fog should tend towards if the skybox ever fails to load.
        up = a[: a.shape[0] // 2]
        avg = up.reshape(-1, 3).mean(axis=0)
        avg = avg * (1.45 / max(np.percentile(lum(up)[lum(up) > 0], 97.0), 1e-6))
        avg = np.power(1.0 - np.exp(-avg), 1.0 / 2.2)

        img = Image.fromarray(tonemap(a))
        if img.size != (OUT_W, OUT_H):
            img = img.resize((OUT_W, OUT_H), Image.LANCZOS)
        stem = n[:-4].replace('_4k', '')
        dst = os.path.join(SKY, stem + '.jpg')
        img.save(dst, quality=92, subsampling=0, optimize=True)
        os.remove(path)

        print('%-38s sun(%.1f, %.1f)  tint(%.3f,%.3f,%.3f)  sky(%.3f,%.3f,%.3f)  %.1f MB'
              % (stem, pitch, yaw, tint[0], tint[1], tint[2],
                 avg[0], avg[1], avg[2], os.path.getsize(dst) / 1048576.0))


main()
