"""A numpy triangle rasterizer: per pixel, the nearest covering triangle and its barycentrics.

Used twice by the pipeline: in camera space (the concept's view: which surface each concept pixel sees, for the
projection's visibility test) and in UV space (which surface point each texel of a new layout stands for, for the
bakes). Pixel centres are at (x + 0.5, y + 0.5).
"""
import numpy as np


def rasterize(xy, depth, faces, width, height, chunk=20000):
    """xy: (V, 2) pixel coordinates; depth: (V,) (smaller is nearer; pass zeros for a UV layout);
    returns tri (H, W) int32 (-1 empty), bary (H, W, 3) float32, z (H, W) float32."""
    tri_buf = np.full(width * height, -1, np.int32)
    z_buf = np.full(width * height, np.inf, np.float32)
    bary_buf = np.zeros((width * height, 3), np.float32)
    P = xy[faces]                      # (T, 3, 2)
    Z = depth[faces]                   # (T, 3)
    lo = np.floor(P.min(1) - 0.5).astype(np.int64)
    hi = np.ceil(P.max(1) - 0.5).astype(np.int64)
    lo = np.maximum(lo, 0)
    hi[:, 0] = np.minimum(hi[:, 0], width - 1)
    hi[:, 1] = np.minimum(hi[:, 1], height - 1)
    ext = hi - lo + 1
    valid = (ext > 0).all(1)
    ids = np.nonzero(valid)[0]
    size = np.maximum(ext[ids, 0], ext[ids, 1])
    # Batch triangles of similar bounding-box size so each batch is one dense grid.
    for bound in np.unique(np.minimum(np.ceil(np.log2(np.maximum(size, 1))).astype(int), 12)):
        k = 1 << bound
        sel = ids[np.minimum(np.ceil(np.log2(np.maximum(size, 1))).astype(int), 12) == bound]
        gy, gx = np.mgrid[0:k, 0:k]
        gx, gy = gx.ravel(), gy.ravel()
        per = max(1, chunk * 64 // (k * k))
        for s in range(0, len(sel), per):
            t = sel[s:s + per]
            px = lo[t, 0:1] + gx[None, :]
            py = lo[t, 1:2] + gy[None, :]
            inside_box = (px <= hi[t, 0:1]) & (py <= hi[t, 1:2])
            cx, cy = px + 0.5, py + 0.5
            a, b, c = P[t, 0], P[t, 1], P[t, 2]
            den = (b[:, 1] - c[:, 1]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 1] - c[:, 1])
            ok = np.abs(den) > 1e-12
            den = np.where(ok, den, 1.0)[:, None]
            w0 = ((b[:, 1:2] - c[:, 1:2]) * (cx - c[:, 0:1]) + (c[:, 0:1] - b[:, 0:1]) * (cy - c[:, 1:2])) / den
            w1 = ((c[:, 1:2] - a[:, 1:2]) * (cx - c[:, 0:1]) + (a[:, 0:1] - c[:, 0:1]) * (cy - c[:, 1:2])) / den
            w2 = 1.0 - w0 - w1
            eps = -1e-6
            inside = inside_box & (w0 >= eps) & (w1 >= eps) & (w2 >= eps) & ok[:, None]
            if not inside.any():
                continue
            ti, gi = np.nonzero(inside)
            pix = (py[ti, gi] * width + px[ti, gi]).astype(np.int64)
            ww = np.stack([w0[ti, gi], w1[ti, gi], w2[ti, gi]], 1).astype(np.float32)
            zz = (ww * Z[t[ti]]).sum(1).astype(np.float32)
            order = np.lexsort((zz, pix))
            pix, zz, ww, tt = pix[order], zz[order], ww[order], t[ti][order]
            first = np.ones(len(pix), bool)
            first[1:] = pix[1:] != pix[:-1]
            pix, zz, ww, tt = pix[first], zz[first], ww[first], tt[first]
            better = zz < z_buf[pix]
            pix, zz, ww, tt = pix[better], zz[better], ww[better], tt[better]
            z_buf[pix] = zz
            tri_buf[pix] = tt
            bary_buf[pix] = ww
    return tri_buf.reshape(height, width), bary_buf.reshape(height, width, 3), z_buf.reshape(height, width)


def interpolate(attr, faces, tri, bary):
    """Per-pixel interpolation of a per-vertex attribute (V, C) through rasterize()'s output; empty pixels are 0."""
    out = np.zeros(tri.shape + (attr.shape[1],), np.float32)
    m = tri >= 0
    f = faces[tri[m]]
    out[m] = (attr[f] * bary[m][:, :, None]).sum(1)
    return out
