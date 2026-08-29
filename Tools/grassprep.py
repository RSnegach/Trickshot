"""Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.

Source: ambientCG Grass005 (CC0), a short clean lawn. This replaced Grass001, which is
wild meadow: broad dock leaves and seed heads, which stretched across a football pitch
read as weeds rather than turf.

Two outputs, both consumed by Turf.Detail as the Standard shader's DETAIL layer:

  Grass_Detail.jpg        albedo, mean-normalised to mid grey
  Grass_DetailNormal.png  blade normals, renormalised after the downscale

WHY MID GREY. The detail albedo runs through _DETAIL_MULX2, which multiplies by two, so
127.5 is the value that changes nothing. Normalising to it means the map modulates
whatever grass colour the venue asks for instead of stamping the scan's own green over
every pitch in the game.

WHY AO IS FOLDED IN. _OcclusionMap samples at the MAIN texture's tiling, so it cannot
carry a detail-scale map, and the occlusion between blades is most of what makes turf
read as three-dimensional rather than as a green photograph. It goes into the albedo
instead, which is the only detail-tiled slot available.

Roughness and Displacement are not used: the Standard shader's gloss comes from the
generated map in Turf, and there is no tessellation to displace.

Run from the repo root:  python Tools/grassprep.py
"""
import io
import zipfile

import numpy as np
from PIL import Image

SRC = 'Temp/Grass005_2K.zip'
OUT = 'Assets/Resources/Turf'
SIZE = 1024

# Contrast of the detail layer, as a multiple of each source's own spread. Tuned by
# sweeping against the combined std printed at the end; these land it at 15.1 of 255,
# which is +-12% once MULX2 doubles it, and nothing clips at either end. The map this
# replaced sat at 10.7 and read as a faint grey wash over flat paint.
#
# The two are worth weighting separately because they are independent here: measured
# correlation between the colour and the AO is -0.06, so the AO is adding shadow the
# colour map does not already contain, not just steepening what is there.
K_COLOR = 0.95
K_AO = 0.16


def member(z, suffix):
    for n in z.namelist():
        if n.endswith(suffix):
            return n
    raise SystemExit('no member ending %r in %s\n  have: %s' % (suffix, SRC, z.namelist()))


def load(z, suffix, mode='RGB'):
    with z.open(member(z, suffix)) as f:
        im = Image.open(io.BytesIO(f.read())).convert(mode)
    print('  %-22s %s -> %d' % (suffix, im.size, SIZE))
    return np.asarray(im.resize((SIZE, SIZE), Image.LANCZOS), np.float32)


def main():
    z = zipfile.ZipFile(SRC)

    colour = load(z, '_Color.jpg')
    ao = load(z, '_AmbientOcclusion.jpg', 'L')

    # Centre both on their own mean, so neither contributes a net brightness shift, then
    # recombine around mid grey. Per channel on the colour, to keep the chroma variation
    # between yellow-green and blue-green that makes real turf look alive.
    detail = np.empty_like(colour)
    for i in range(3):
        detail[..., i] = 127.5 + K_COLOR * (colour[..., i] - colour[..., i].mean())
    detail += (K_AO * (ao - ao.mean()))[..., None]
    detail = np.clip(detail, 0, 255)

    Image.fromarray(detail.astype(np.uint8)).save(
        OUT + '/Grass_Detail.jpg', quality=94, subsampling=0)

    # Renormalise: averaging neighbouring normals during the downscale shortens the
    # vectors, which reads as a flatter surface than the scan actually is.
    n = load(z, '_NormalGL.jpg') / 127.5 - 1.0
    n /= np.maximum(1e-6, np.linalg.norm(n, axis=-1, keepdims=True))
    Image.fromarray(np.clip((n + 1.0) * 127.5, 0, 255).astype(np.uint8)).save(
        OUT + '/Grass_DetailNormal.png')

    g = detail.mean(-1)
    print('\n  detail  mean %.1f  std %.2f  (%.0f..%.0f)   per channel std %s'
          % (g.mean(), g.std(), g.min(), g.max(),
             np.round([detail[..., i].std() for i in range(3)], 1)))
    print('  MULX2 swing about mid grey: +-%.1f%%' % (g.std() / 127.5 * 100))


if __name__ == '__main__':
    main()
