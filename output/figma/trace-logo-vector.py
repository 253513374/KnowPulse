"""Trace the existing logo into smooth, separately editable SVG shapes."""
from collections import deque
from pathlib import Path
import json
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
RGB = np.asarray(Image.open(ROOT.parent / 'imagegen/zhimai-learning-avatar.png').convert('RGB'))
PIX = RGB.astype(np.int16)
H, W = RGB.shape[:2]
BLUE = (PIX[:, :, 2] - PIX[:, :, 0] > 60) & (PIX[:, :, 2] - PIX[:, :, 1] > 45) & (PIX[:, :, 2] > 130)
GOLD = (PIX[:, :, 0] > 200) & (PIX[:, :, 1] > 100) & (PIX[:, :, 1] < 220) & (PIX[:, :, 2] < 100)


def components(mask):
    remaining = mask.copy()
    result = []
    for y0, x0 in zip(*np.where(mask)):
        if not remaining[y0, x0]:
            continue
        queue, pixels = deque([(int(y0), int(x0))]), []
        remaining[y0, x0] = False
        while queue:
            y, x = queue.popleft()
            pixels.append((y, x))
            for ny, nx in ((y-1, x), (y+1, x), (y, x-1), (y, x+1)):
                if 0 <= ny < H and 0 <= nx < W and remaining[ny, nx]:
                    remaining[ny, nx] = False
                    queue.append((ny, nx))
        if len(pixels) > 200:
            region = np.zeros_like(mask)
            yy, xx = np.asarray(pixels).T
            region[yy, xx] = True
            result.append((len(pixels), (int(xx.min()), int(yy.min()), int(xx.max()), int(yy.max())), region))
    return sorted(result, key=lambda item: item[0], reverse=True)


def contour(mask):
    edges = {}
    specifications = [(mask & ~np.roll(mask, 1, 0), (0, 0), (1, 0)),
                      (mask & ~np.roll(mask, -1, 1), (1, 0), (1, 1)),
                      (mask & ~np.roll(mask, -1, 0), (1, 1), (0, 1)),
                      (mask & ~np.roll(mask, 1, 1), (0, 1), (0, 0))]
    for boundary, a, b in specifications:
        for y, x in zip(*np.where(boundary)):
            edges.setdefault((int(x+a[0]), int(y+a[1])), []).append((int(x+b[0]), int(y+b[1])))
    loops = []
    while edges:
        start = min(edges, key=lambda p: (p[1], p[0]))
        point, points = start, [start]
        while True:
            next_point = edges[point].pop()
            if not edges[point]:
                del edges[point]
            points.append(next_point)
            point = next_point
            if point == start:
                break
        loops.append(np.asarray(points, dtype=float))
    return max(loops, key=lambda p: abs(np.sum(p[:-1, 0]*p[1:, 1] - p[1:, 0]*p[:-1, 1])))


def unit(v):
    return v/max(np.linalg.norm(v), 1e-9)


def fit_cubics(points, left, right, tolerance=.8, depth=0):
    p0, p3 = points[0], points[-1]
    if len(points) <= 2:
        distance = np.linalg.norm(p3-p0)/3
        return [np.array([p0, p0+left*distance, p3+right*distance, p3])]
    u = np.r_[0, np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))]
    u /= u[-1]
    b0, b1, b2, b3 = (1-u)**3, 3*u*(1-u)**2, 3*u*u*(1-u), u**3
    a = np.stack([b1[:, None]*left, b2[:, None]*right], axis=2).reshape(-1, 2)
    residual = points-(b0+b1)[:, None]*p0-(b2+b3)[:, None]*p3
    alpha = np.linalg.lstsq(a, residual.reshape(-1), rcond=None)[0]
    if min(alpha) < 1e-6 or max(alpha) > 3*np.sum(np.linalg.norm(np.diff(points, axis=0), axis=1)):
        alpha[:] = np.linalg.norm(p3-p0)/3
    curve = np.array([p0, p0+alpha[0]*left, p3+alpha[1]*right, p3])
    fitted = b0[:, None]*curve[0]+b1[:, None]*curve[1]+b2[:, None]*curve[2]+b3[:, None]*curve[3]
    errors = np.sum((fitted-points)**2, axis=1)
    split = max(1, min(len(points)-2, int(np.argmax(errors))))
    if errors[split] <= tolerance*tolerance or depth > 24:
        return [curve]
    middle = unit(points[max(0, split-2)]-points[min(len(points)-1, split+2)])
    return fit_cubics(points[:split+1], left, middle, tolerance, depth+1)+fit_cubics(points[split:], -middle, right, tolerance, depth+1)


def path_data(mask):
    original = contour(mask)
    ring = original[:-1]
    offsets = np.arange(-9, 10)
    weights = np.exp(-offsets**2/18)
    weights /= weights.sum()
    ring = sum(weight*np.roll(ring, int(offset), axis=0) for offset, weight in zip(offsets, weights))
    ring = np.vstack([ring, ring[0]])
    distance = np.r_[0, np.cumsum(np.linalg.norm(np.diff(ring, axis=0), axis=1))]
    samples = np.linspace(0, distance[-1], max(12, int(distance[-1]/4)))
    points = np.column_stack([np.interp(samples, distance, ring[:, axis]) for axis in (0, 1)])
    tangent = unit(points[2]-points[-3])
    curves = fit_cubics(points, tangent, -tangent)
    start = curves[0][0]
    data = f'M {start[0]:.2f} {start[1]:.2f}'
    for curve in curves:
        data += ' C ' + ' '.join(f'{value:.2f}' for value in curve[1:].reshape(-1))
    assert 1 < len(curves) < 150
    t = np.linspace(0, 1, 100)[:, None]
    rendered = np.vstack([(1-t)**3*c[0]+3*t*(1-t)**2*c[1]+3*t*t*(1-t)*c[2]+t**3*c[3] for c in curves])
    assert np.all(rendered.min(axis=0) >= points.min(axis=0)-3)
    assert np.all(rendered.max(axis=0) <= points.max(axis=0)+3)
    return data+' Z', len(curves)


def gradient(name, mask):
    inside = mask.copy()
    for _ in range(5):
        inside &= np.roll(inside, 1, 0) & np.roll(inside, -1, 0) & np.roll(inside, 1, 1) & np.roll(inside, -1, 1)
    y, x = np.where(inside)
    if len(x) < 30:
        y, x = np.where(mask)
    xy = np.column_stack([x+.5, y+.5])
    center = xy.mean(axis=0)
    coefficients = np.linalg.lstsq(np.column_stack([np.ones(len(x)), (xy-center)/W]), RGB[y, x], rcond=None)[0]
    direction = np.linalg.svd(coefficients[1:], full_matrices=False)[0][:, 0]
    projection = (xy-center)@direction
    ends = [center+direction*projection.min(), center+direction*projection.max()]
    colors = [np.clip(np.rint(coefficients[0]+(point-center)/W@coefficients[1:]), 0, 255).astype(int) for point in ends]
    color_hex = ['#'+''.join(f'{channel:02X}' for channel in color) for color in colors]
    definition = (f'<linearGradient id="{name}-paint" gradientUnits="userSpaceOnUse" x1="{ends[0][0]:.2f}" y1="{ends[0][1]:.2f}" x2="{ends[1][0]:.2f}" y2="{ends[1][1]:.2f}">'
                  f'<stop stop-color="{color_hex[0]}"/><stop offset="1" stop-color="{color_hex[1]}"/></linearGradient>')
    return definition, color_hex


blue_parts = components(BLUE)
assert len(blue_parts) == 3, [(n, box) for n, box, _ in blue_parts]
network = min(blue_parts, key=lambda item: item[1][1])[2]
pages = sorted([item for item in blue_parts if item[1][1] > 500], key=lambda item: item[1][0])
assert len(pages) == 2
star = components(GOLD)[0][2]
boundary = network & ~(np.roll(network, 1, 0) & np.roll(network, -1, 0) & np.roll(network, 1, 1) & np.roll(network, -1, 1))
by, bx = np.where(boundary)
circle_regions = {'node-top': (bx>540)&(bx<710)&(by>220)&(by<307),
                  'node-left': (bx>280)&(bx<351)&(by>410)&(by<550),
                  'node-right': (bx>903)&(bx<975)&(by>410)&(by<550)}
circles = {}
for name, select in circle_regions.items():
    x, y = bx[select]+.5, by[select]+.5
    a, b, c = np.linalg.lstsq(np.column_stack([x, y, np.ones(len(x))]), -(x*x+y*y), rcond=None)[0]
    cx, cy = -a/2, -b/2
    radius = np.sqrt(cx*cx+cy*cy-c)
    rms = np.sqrt(np.mean((np.hypot(x-cx, y-cy)-radius)**2))
    assert rms < 1
    circles[name] = (float(cx), float(cy), float(radius))

yy, xx = np.indices(BLUE.shape)
left_x, left_y, _ = circles['node-left']
top_x, top_y, _ = circles['node-top']
right_x, right_y, _ = circles['node-right']
paths = [('book-left', pages[0][2]), ('book-right', pages[1][2]),
         ('connection-upper-left', network & (xx>=int(left_x)) & (xx<=int(top_x)) & (yy>=int(top_y)) & (yy<=int(left_y))),
         ('connection-upper-right', network & (xx>=int(top_x)) & (xx<=int(right_x)) & (yy>=int(top_y)) & (yy<=int(right_y))),
         ('connection-lower-left', network & (xx>=int(left_x)) & (xx<=int(top_x)) & (yy>=int(left_y))),
         ('connection-lower-right', network & (xx>=int(top_x)) & (xx<=int(right_x)) & (yy>=int(right_y)))]
definitions, shapes, metadata = [], [], []
for name, mask in paths:
    data, segments = path_data(mask)
    definition, colors = gradient(name, mask)
    definitions.append(definition)
    shapes.append(f'<path id="{name}" d="{data}" fill="url(#{name}-paint)"/>')
    metadata.append({'name': name, 'cubicSegments': segments, 'colors': colors})
for name, (cx, cy, radius) in circles.items():
    mask = BLUE & ((xx+.5-cx)**2+(yy+.5-cy)**2 < (radius-2)**2)
    definition, colors = gradient(name, mask)
    definitions.append(definition)
    shapes.append(f'<circle id="{name}" cx="{cx:.3f}" cy="{cy:.3f}" r="{radius:.3f}" fill="url(#{name}-paint)"/>')
    metadata.append({'name': name, 'center': [cx, cy], 'radius': radius, 'colors': colors})
data, segments = path_data(star)
definition, colors = gradient('ai-spark', star)
definitions.append(definition)
shapes.append(f'<path id="ai-spark" d="{data}" fill="url(#ai-spark-paint)"/>')
metadata.append({'name': 'ai-spark', 'cubicSegments': segments, 'colors': colors})
svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">\n<defs>\n'+ '\n'.join(definitions) + '\n</defs>\n' + '\n'.join(shapes) + '\n</svg>\n'
assert '<image' not in svg and len(shapes) == 10
(ROOT / 'zhimai-learning-logo-vector.svg').write_text(svg, encoding='utf-8')
(ROOT / 'logo-vector-trace.json').write_text(json.dumps({'size': [W,H], 'shapes': metadata}, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'file': str(ROOT / 'zhimai-learning-logo-vector.svg'), 'shapeCount': len(shapes), 'bytes': len(svg), 'shapes': metadata}, ensure_ascii=False))
