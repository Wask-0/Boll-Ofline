# Генератор 35 карт (7 на вид) -> Assets/Resources/maps/<вид>/<id>.json
# Запуск: python tools/gen_maps.py   (метры, стол центрован в (0,0), ось расстановки +x, дом слева)
import json, math, os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Resources', 'maps')
KINDS = {
    # kind: (L, W, ball diameter, throat, k-bumper base, mover speed)
    'eight':     (2.54, 1.27, 0.05715, 0.115, 1.15, 0.30),
    'nine':      (2.54, 1.27, 0.05715, 0.115, 1.20, 0.35),
    'american':  (3.55, 1.775, 0.068, 0.076, 1.25, 0.30),
    'siberian':  (3.55, 1.775, 0.068, 0.076, 1.10, 0.40),
    'snooker':   (3.569, 1.778, 0.0525, 0.086, 1.30, 0.30),
}
CAPTURE = 1.075
r2 = lambda v: round(v, 4)
P = lambda x, y: [r2(x), r2(y)]


def pockets_for(shape, L, W, R, thr, c, waist):
    rad = thr * CAPTURE / 2
    out = []
    t = R * math.sqrt(2) * 0.9
    hx, hy = L / 2, W / 2
    ymid = hy - (waist if shape == 'waist' else 0) - R + 0.3 * rad
    out += [(0, ymid), (0, -ymid)]
    if shape == 'chamfer':
        s = 1 / math.sqrt(2)
        for sx in (-1, 1):
            for sy in (-1, 1):
                mx, my = sx * (hx - c / 2), sy * (hy - c / 2)
                k = R - 0.3 * rad
                out.append((mx - sx * s * k, my - sy * s * k))
    else:
        for sx in (-1, 1):
            for sy in (-1, 1):
                out.append((sx * (hx - t), sy * (hy - t)))
    return out


def outline_for(shape, L, W, c, waist):
    hx, hy = L / 2, W / 2
    if shape == 'chamfer':
        return [(-hx + c, -hy), (hx - c, -hy), (hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c)]
    if shape == 'waist':
        w = waist
        return [(-hx, -hy), (-0.5, -hy), (-0.3, -hy + w), (0.3, -hy + w), (0.5, -hy), (hx, -hy), (hx, hy), (0.5, hy), (0.3, hy - w), (-0.3, hy - w), (-0.5, hy), (-hx, hy)]
    return [(-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)]


def layout_for(kind, L, W, R, c=0.0):
    hx, hy = L / 2, W / 2
    m = max(0.12, c / 2 + 0.07)
    lay = {}
    if kind == 'snooker':
        bx = -hx + 0.737
        arc = [(bx - 0.292 * math.sin(a), 0.292 * math.cos(a)) for a in [math.pi * i / 12 for i in range(0, 13)]]
        lay['home'] = [P(*p) for p in arc]
        lay['homeLineA'] = P(bx, -0.292 - 0.03); lay['homeLineB'] = P(bx, 0.292 + 0.03)
        pinkx = hx / 2
        lay['rackPoint'] = P(pinkx + 2 * R * 1.0001 + 0.0005, 0)
        lay['marks'] = {'yellow': P(bx, 0.292), 'green': P(bx, -0.292), 'brown': P(bx, 0), 'blue': P(0, 0),
                        'pink': P(pinkx, 0), 'black': P(hx - 0.324, 0)}
    else:
        hxl = -L / 4
        lay['home'] = [P(-hx + m, -hy + m), P(hxl, -hy + m), P(hxl, hy - m), P(-hx + m, hy - m)]
        lay['homeLineA'] = P(hxl, -hy + 0.03); lay['homeLineB'] = P(hxl, hy - 0.03)
        lay['rackPoint'] = P(L / 4, 0)
        if kind in ('american', 'siberian'):
            lay['marks'] = {'back': P(L / 4, 0)}
    lay['rackAxis'] = [1, 0]
    lay['returnPoints'] = [P(-0.3 * L, 0.26 * W), P(-0.3 * L, -0.26 * W), P(0.3 * L, 0.3 * W), P(0.3 * L, -0.3 * W)]
    return lay


def build(kind, n):
    L, W, D, thr, kb, spd = KINDS[kind]
    R = D / 2
    hx, hy = L / 2, W / 2
    shape, waist, c = 'rect', 0.0, 0.0
    obs = []
    names = ['Классика', 'Бамперы', 'Перегородки', 'Маятник', 'Карусель', 'Срезанные углы', 'Песочные часы']
    name = names[n - 1]
    # небольшие отличия по виду, чтобы карты разных видов не повторялись
    jitter = {'eight': 0.0, 'nine': 0.03, 'american': -0.02, 'siberian': 0.02, 'snooker': -0.03}[kind]
    if n == 2:
        rb = 1.3 * R
        obs = [{'type': 'bumper', 'c': P(0.08 * L + jitter, 0.25 * W), 'r': r2(rb), 'k': kb},
               {'type': 'bumper', 'c': P(-0.08 * L + jitter, -0.25 * W), 'r': r2(rb), 'k': kb}]
    elif n == 3:
        for sy in (1, -1):
            obs.append({'type': 'wall', 'a': P(-0.1 * L + jitter, sy * 0.22 * W), 'b': P(0.1 * L + jitter, sy * 0.22 * W)})
    elif n == 4:
        # горизонтальная планка ездит поперёк стола: линия «дом — пирамида» открыта часть времени
        lw = 0.2 * L; b = 0.2 * W
        cx = -0.12 * L if kind == 'snooker' else jitter
        obs = [{'type': 'mover', 'a': P(cx - lw / 2, -b), 'b': P(cx + lw / 2, -b), 'speed': spd, 'mode': 'pingpong', 'path': [[0, 0], [0, r2(2 * b)]]}]
    elif n == 5:
        rho = 0.17 * W; lw = 0.2 * W
        cx, cy = jitter, 0
        obs = [{'type': 'mover', 'a': P(cx + rho, cy - lw / 2), 'b': P(cx + rho, cy + lw / 2), 'speed': spd, 'mode': 'circle', 'center': P(cx, cy), 'radius': r2(rho)}]
    elif n == 6:
        shape, c = 'chamfer', 0.14 * W
        obs = [{'type': 'bumper', 'c': P(0.05 * L, 0.27 * W), 'r': r2(1.2 * R), 'k': kb},
               {'type': 'bumper', 'c': P(-0.05 * L, -0.27 * W), 'r': r2(1.2 * R), 'k': kb}]
    elif n == 7:
        shape, waist = 'waist', 0.08 * W
        lw = 0.14 * W; b = 0.05 * L
        obs = [{'type': 'wall', 'a': P(-0.07 * L, 0.0), 'b': P(-0.07 * L, 0.0)}][:0]
    pk = pockets_for(shape, L, W, R, thr, c, waist)
    m = {
        'id': f'{kind}_{n}', 'name': name, 'kind': kind,
        'outline': [P(*p) for p in outline_for(shape, L, W, c, waist)],
        'pockets': [{'pos': P(*p)} for p in pk],
        'obstacles': obs,
        'layout': layout_for(kind, L, W, R, c),
    }
    return m


if __name__ == '__main__':
    for kind in KINDS:
        d = os.path.join(ROOT, kind)
        os.makedirs(d, exist_ok=True)
        for n in range(1, 8):
            m = build(kind, n)
            with open(os.path.join(d, f'{kind}_{n}.json'), 'w', encoding='utf-8') as f:
                json.dump(m, f, ensure_ascii=False, indent=1)
    print('ok')
