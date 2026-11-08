"""The Storm Regalia winged helm skin (objects 4942, pskins 5800 sf_wingedhelm.dds) with wings painted from the mesh only.

The concept kit's skin (Stormforged-armor-concepts/v20-polished-set, paint_wingedhelm_v24.py) painted the shell, brow band,
gem and crest from the mesh, but took its wing feathers from the stock ToA winged helm texture, recoloured. That made the
skin part retail art, so it stayed out of the release client patch (2026-11-08). This painter keeps the concept skin's
shell side (u < 0.64, all ours; textures/helm_shell.png) and paints new wings from the helm mesh: each wing island fans
out from where it joins the shell, in feather plates with a groove between them, a quill and fine barbs, in the set's
polished silver with the gold edge. No stock texture is read.

    python tools/sovereign-art/storm/paint_storm_helm.py          # textures/helm.png and .work/storm/helm_preview.png
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "tara"))
import paint_tara as T  # noqa: E402  (pipeline paths and the polished Canvas.layer)
from paint_tara import np, Image, P5, Canvas, ramp, lit, fbm3, smoothstep  # noqa: E402
from scipy.ndimage import label  # noqa: E402
import helm_fit  # noqa: E402
import render as R  # noqa: E402
import bake_arrays  # noqa: E402
import paths  # noqa: E402

SIZE = (512, 256)
SPLIT = 0.64                       # the stock layout: wings at u > 0.64
FEATHERS = 7
# the Stormforged set's palettes (concept kit paint_v15a)
SILVER = [(14, 15, 18), (52, 55, 62), (112, 116, 126), (182, 186, 194), (238, 240, 244)]
GOLD = [(30, 22, 10), (86, 64, 30), (144, 114, 60), (198, 170, 110), (240, 226, 182)]
OUT = paths.WORK / "storm"


def helm_tm():
    v, nn, uv, f = R.mesh(helm_fit.helm("HN", "m"))
    return bake_arrays.bake(v, nn, uv, f, size=512)


def wing_parts(C, wings):
    """The wings' separate 3D parts (each wing of each side; both faces of a wing are one part), as texel masks."""
    pts = C.p[wings]
    lo, hi = pts.min(0), pts.max(0)
    cell = float((hi - lo).max()) / 70.0
    vox = np.floor((pts - lo) / cell).astype(int)
    grid = np.zeros(vox.max(0) + 1, bool)
    grid[tuple(vox.T)] = True
    lab, count = label(grid, structure=np.ones((3, 3, 3)))
    owner = lab[tuple(vox.T)]
    parts = []
    for i in range(1, count + 1):
        mask = np.zeros(wings.shape, bool)
        mask[wings] = owner == i
        if mask.sum() >= 40:
            parts.append(mask)
    return parts


def wing_fan(C, part, shell):
    """(feather coordinate, distance from the root) on one wing part. The root is where the part lies closest to the
    shell; feathers fan out from it in the part's own plane (its two main 3D directions), so both faces agree."""
    S = C.cov.shape[0]
    q, sp = C.p[part], C.p[shell]
    sp = sp[:: max(1, len(sp) // 3000)]
    d = np.full(len(q), np.inf)
    for start in range(0, len(q), 2000):
        chunk = q[start:start + 2000]
        d[start:start + 2000] = np.sqrt(((chunk[:, None, :] - sp[None, :, :]) ** 2).sum(-1)).min(1)
    _, _, axes = np.linalg.svd(q - q.mean(0), full_matrices=False)
    # The base is the end of the wing (along its length) that lies nearer the shell: a wing that hugs the shell along
    # its length would otherwise fan out from its middle.
    s = (q - q.mean(0)) @ axes[0]
    ends = [s <= np.percentile(s, 10), s >= np.percentile(s, 90)]
    base = min(ends, key=lambda end: d[end].mean())
    root = q[base].mean(0)
    a, b = (q - root) @ axes[0], (q - root) @ axes[1]
    angle = np.arctan2(b, a)
    mid = np.arctan2(np.sin(angle).mean(), np.cos(angle).mean())   # unwrap round the part's own direction
    angle = (angle - mid + np.pi) % (2 * np.pi) - np.pi
    lo, hi = np.percentile(angle, 1), np.percentile(angle, 99)
    reach = np.hypot(a, b)
    fan, rad = np.zeros((S, S)), np.zeros((S, S))
    fan[part] = np.clip((angle - lo) / max(hi - lo, 1e-3), 0, 0.9999) * FEATHERS
    rad[part] = reach / max(np.percentile(reach, 99), 1e-3)
    return fan, np.clip(rad, 0, 1)


def paint_wings(C):
    S = C.cov.shape[0]
    uu = np.arange(S)[None, :].repeat(S, 0) / S
    wings = (uu > SPLIT) & C.cov
    shell = C.cov & ~wings
    t = np.full((S, S), 0.35)
    for i, island in enumerate(wing_parts(C, wings), 1):
        fan, rad = wing_fan(C, island, shell)
        idx, frac = np.floor(fan), fan - np.floor(fan)
        across = np.abs(frac - 0.5) * 2                                    # 0 at the quill, 1 at the plate's edge
        profile = 1.0 - across ** 2.2                                       # each feather a rounded, polished plate
        groove = smoothstep(0.86, 0.98, across)                             # the dark gap between plates
        quill = (1 - smoothstep(0.0, 0.07, across)) * (1 - 0.6 * rad)       # a bright quill, fading to the tip
        barbs = 0.5 + 0.5 * np.sin((frac - 0.5) * 9.0 + rad * 46.0 * (1 + 0.15 * np.sin(idx * 1.7)))
        tone = 0.06 * np.sin(idx * 2.39 + i)                                # plates catch the light a little differently
        tip = smoothstep(0.82, 1.0, rad) * 0.18                             # tips a shade darker toward the edge
        grain = fbm3(C.p, 0.5, seed=707 + i)
        v = (0.12 + 0.50 * profile + 0.08 * (barbs - 0.5) * (1 - across) + 0.22 * quill - 0.70 * groove + tone - tip
             + 0.05 * (grain - 0.5) + 0.22 * (lit(C.n) - 0.5))
        t[island] = v[island]
    C.col = np.where(wings[..., None], ramp(np.clip(t, 0, 1), SILVER), C.col)
    C.layer(wings & (C.dist(wings) < 3.2), GOLD, outline=0.5, rim=0.8, pillow=1.3, spec=0.85)
    return C, wings


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    C, _ = paint_wings(Canvas(helm_tm()))
    painted = C.finish(SIZE)
    base = Image.open(HERE / "textures" / "helm_shell.png").convert("RGB")
    assert base.size == SIZE
    out = np.asarray(base).copy()
    cut = int(np.ceil(SPLIT * SIZE[0]))
    out[:, cut:] = np.asarray(painted.convert("RGB"))[:, cut:]
    img = Image.fromarray(out)
    img.save(HERE / "textures" / "helm.png")
    cells = []
    for race, sex, head in (("HN", "m", "nor_m_head01"), ("HN", "f", "nor_f_head01"), ("Tro", "m", "tro_m_head01")):
        cells.append(R.render([(head, (176, 140, 116)), (helm_fit.helm(race, sex), img)],
                              views=[R.VIEWS[0], R.VIEWS[1], R.VIEWS[2]], width=240, height=300, label=f"{race} {sex}"))
    R.stack(cells).save(OUT / "helm_preview.png")
    print("wrote", HERE / "textures" / "helm.png", "and", OUT / "helm_preview.png")


if __name__ == "__main__":
    main()
