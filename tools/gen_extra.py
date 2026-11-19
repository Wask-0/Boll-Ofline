# 10 нестандартных карт (кроме снукера): id <вид>_8 .. <вид>_17. Подключается из gen_maps.py.
# Координаты форм нормированы: x = u * L/2, y = v * W/2.
import math
from gen_maps import KINDS, CAPTURE, P, r2


def B(u, v, k=1.3): return ('b', u, v, k)
def Wl(u1, v1, u2, v2): return ('w', u1, v1, u2, v2)
def M(u, v, rho, lw): return ('mc', u, v, rho, lw)
def sym(f): return [f(1), f(-1)]


SHAPES = [
    dict(name='Крест', hv=0.5,
         poly=[(-1, -.55), (-.35, -.55), (-.35, -1), (.35, -1), (.35, -.55), (1, -.55), (1, .55), (.35, .55), (.35, 1), (-.35, 1), (-.35, .55), (-1, .55)],
         corners=[(-1, -.55), (1, -.55), (1, .55), (-1, .55)], edges=[((0, -1), (0, 1)), ((0, 1), (0, -1))],
         obs=sym(lambda s: B(0, s * .75, 1.3)) + sym(lambda s: B(-.12, s * .3, 1.2)) + sym(lambda s: Wl(-.28, s * .18, -.28, s * .42)) + sym(lambda s: Wl(.12, s * .2, .26, s * .38))),
    dict(name='Восьмиугольник', hv=0.45,
         poly=[(-.7, -1), (.7, -1), (1, -.4), (1, .4), (.7, 1), (-.7, 1), (-1, .4), (-1, -.4)],
         corners=[], edges=[((0, -1), (0, 1)), ((0, 1), (0, -1))], diag=True,
         obs=[M(0, 0, 0.15, 0.18)] + sym(lambda s: B(.35, s * .62, 1.3)) + sym(lambda s: B(-.35, s * .62, 1.3)) + sym(lambda s: Wl(.25, s * .85, .25, s * .6))),
    dict(name='Гантель', hv=0.85,
         poly=[(-1, -1), (-.25, -1), (-.25, -.35), (.25, -.35), (.25, -1), (1, -1), (1, 1), (.25, 1), (.25, .35), (-.25, .35), (-.25, 1), (-1, 1)],
         corners=[(-1, -1), (1, -1), (1, 1), (-1, 1)], edges=[((-.62, -1), (0, 1)), ((-.62, 1), (0, -1)), ((.62, -1), (0, 1)), ((.62, 1), (0, -1))],
         obs=sym(lambda s: Wl(-.5, s * .45, -.5, s * .75)) + sym(lambda s: B(.62, s * .5, 1.3)) + sym(lambda s: Wl(.85, s * .5, .85, s * .8))),
    dict(name='Бухты', hv=0.85,
         poly=[(-1, -1), (-.25, -1), (-.25, -.7), (.05, -.7), (.05, -1), (1, -1), (1, 1), (.05, 1), (.05, .7), (-.25, .7), (-.25, 1), (-1, 1)],
         corners=[(-1, -1), (1, -1), (1, 1), (-1, 1)], edges=[((-.62, -1), (0, 1)), ((-.62, 1), (0, -1)), ((.55, -1), (0, 1)), ((.55, 1), (0, -1))],
         obs=sym(lambda s: B(-.1, s * .3, 1.2)) + sym(lambda s: Wl(.9, s * .35, .9, s * .7)) + sym(lambda s: B(.3, s * .55, 1.2)) + sym(lambda s: Wl(-.45, s * .3, -.45, s * .55))),
    dict(name='Г-образный', hv=0.85,
         poly=[(-1, -1), (1, -1), (1, .45), (.15, .45), (.15, 1), (-1, 1)],
         corners=[(-1, -1), (1, -1), (1, .45), (-1, 1), (.15, 1)], edges=[((0, -1), (0, 1)), ((-.5, 1), (0, -1))],
         obs=[B(-.15, .6, 1.3), Wl(-.38, -.42, -.38, -.72), B(.45, -.72, 1.3), Wl(.75, -.55, .9, -.55), B(-.1, -.55, 1.2), Wl(.05, .2, .05, .4), Wl(-.2, .3, -.2, .12)]),
    dict(name='Т-образный', hv=0.4,
         poly=[(-1, -.5), (-.1, -.5), (-.1, -1), (1, -1), (1, 1), (-.1, 1), (-.1, .5), (-1, .5)],
         corners=[(-1, -.5), (1, -1), (1, 1), (-1, .5), (-.1, -1), (-.1, 1)], edges=[((.45, -1), (0, 1)), ((.45, 1), (0, -1))],
         obs=sym(lambda s: B(.05, s * .72, 1.3)) + sym(lambda s: Wl(.25, s * .55, .45, s * .8)) + sym(lambda s: B(.88, s * .55, 1.2)) + sym(lambda s: Wl(.65, s * .62, .8, s * .62))),
    dict(name='Ступеньки', hv=0.5,
         poly=[(-1, -.6), (-.6, -.6), (-.6, -.8), (-.2, -.8), (-.2, -1), (1, -1), (1, 1), (-.2, 1), (-.2, .8), (-.6, .8), (-.6, .6), (-1, .6)],
         corners=[(-1, -.6), (-.2, -1), (1, -1), (1, 1), (-.2, 1), (-1, .6)], edges=[((.4, -1), (0, 1)), ((.4, 1), (0, -1))],
         obs=sym(lambda s: B(-.05, s * .55, 1.2)) + sym(lambda s: Wl(.12, s * .3, .12, s * .55)) + sym(lambda s: B(.9, s * .5, 1.2)) + sym(lambda s: Wl(.7, s * .65, .85, s * .75))),
    dict(name='Зигзаг', hv=0.5,
         poly=[(-1, -.7), (-.5, -1), (0, -.7), (.5, -1), (1, -.7), (1, .7), (.5, 1), (0, .7), (-.5, 1), (-1, .7)],
         corners=[(-1, -.7), (-.5, -1), (.5, -1), (1, -.7), (1, .7), (.5, 1), (-.5, 1), (-1, .7)], edges=[],
         obs=sym(lambda s: B(-.15, s * .4, 1.2)) + sym(lambda s: B(.25, s * .45, 1.2)) + sym(lambda s: Wl(.8, s * .25, .8, s * .5)) + sym(lambda s: Wl(-.3, s * .18, -.3, s * .38))),
    dict(name='Арка', hv=0.85,
         poly=[(-1, -1), (1, -1), (1, 1), (.3, 1), (.3, .3), (-.3, .3), (-.3, 1), (-1, 1)],
         corners=[(-1, -1), (1, -1), (1, 1), (-1, 1), (.3, 1), (-.3, 1)], edges=[((0, -1), (0, 1))],
         obs=[B(0, -.62, 1.3), Wl(-.15, -.35, .15, -.35)] + sym(lambda s: B(.62, s * .65, 1.3)) + sym(lambda s: Wl(.85, s * .4, .85, s * .75)) + [B(-.5, -.55, 1.2), B(-.5, .55, 1.2)]),
    dict(name='Стрела', hv=0.45,
         poly=[(-1, -.6), (.2, -.6), (.2, -1), (1, 0), (.2, 1), (.2, .6), (-1, .6)],
         corners=[(-1, -.6), (.2, -1), (1, 0), (.2, 1), (-1, .6)], edges=[((-.4, -.6), (0, 1)), ((-.4, .6), (0, -1))],
         ret=[(-.62, .3), (-.62, -.3), (-.2, .25), (-.2, -.25)],
         obs=sym(lambda s: B(-.15, s * .32, 1.1)) + sym(lambda s: Wl(-.35, s * .2, -.35, s * .42)) + sym(lambda s: B(.3, s * .5, 1.2))),
]


def signed_area(pts):
    n = len(pts)
    return sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n)) / 2


def build_extra(kind, idx):
    L, W, D, thr, kb, spd = KINDS[kind]
    R = D / 2; hx, hy = L / 2, W / 2
    sh = SHAPES[idx]
    S = lambda u, v: (u * hx, v * hy)
    poly = [S(*p) for p in sh['poly']]
    if signed_area(poly) < 0: poly.reverse()
    rad = thr * CAPTURE / 2
    pockets = []
    for (u, v) in sh['corners']:
        vx, vy = S(u, v)
        i = min(range(len(poly)), key=lambda k: (poly[k][0] - vx) ** 2 + (poly[k][1] - vy) ** 2)
        vx, vy = poly[i]
        pr, nx = poly[i - 1], poly[(i + 1) % len(poly)]
        e1 = (vx - pr[0], vy - pr[1]); e2 = (nx[0] - vx, nx[1] - vy)
        l1 = math.hypot(*e1); l2 = math.hypot(*e2)
        e1 = (e1[0] / l1, e1[1] / l1); e2 = (e2[0] / l2, e2[1] / l2)
        n1 = (-e1[1], e1[0]); n2 = (-e2[1], e2[0])      # внутренние нормали (контур против часовой)
        bx, by = n1[0] + n2[0], n1[1] + n2[1]; bl = math.hypot(bx, by); bx, by = bx / bl, by / bl
        turn = math.atan2(e1[0] * e2[1] - e1[1] * e2[0], e1[0] * e2[0] + e1[1] * e2[1])
        theta = math.pi - turn
        d = max(1.8 * R, R / math.sin(theta / 2) + 0.15 * R)
        pockets.append((vx + bx * d, vy + by * d))
    for (pu, pv), (nu, nv) in sh['edges']:
        px, py = S(pu, pv); k = R - 0.3 * rad
        pockets.append((px + nu * k, py + nv * k))
    if sh.get('diag'):
        s2 = 1 / math.sqrt(2); k = R - 0.3 * rad
        for sx in (-1, 1):
            for sy in (-1, 1):
                mx, my = S(sx * .85, sy * .7)
                pockets.append((mx - sx * s2 * k, my - sy * s2 * k))
    obs = []
    for o in sh['obs']:
        if o[0] == 'b':
            obs.append({'type': 'bumper', 'c': P(*S(o[1], o[2])), 'r': r2(o[3] * R), 'k': kb})
        elif o[0] == 'w':
            obs.append({'type': 'wall', 'a': P(*S(o[1], o[2])), 'b': P(*S(o[3], o[4]))})
        elif o[0] == 'mc':
            cx, cy = S(o[1], o[2]); rho = o[3] * W; lw = o[4] * W
            obs.append({'type': 'mover', 'a': P(cx + rho, cy - lw / 2), 'b': P(cx + rho, cy + lw / 2),
                        'speed': spd, 'mode': 'circle', 'center': P(cx, cy), 'radius': r2(rho)})
    hv = sh['hv']; m = 0.12
    hxl = -L / 4
    home = [P(-hx + m, -hv * hy), P(hxl, -hv * hy), P(hxl, hv * hy), P(-hx + m, hv * hy)]
    lay = {'home': home, 'homeLineA': P(hxl, -hv * hy - 0.03), 'homeLineB': P(hxl, hv * hy + 0.03),
           'rackPoint': P(L / 4, 0), 'rackAxis': [1, 0]}
    if kind in ('american', 'siberian'): lay['marks'] = {'back': P(L / 4, 0)}
    ret = sh.get('ret', [(-.62, .3), (-.62, -.3), (.7, .25), (.7, -.25)])
    lay['returnPoints'] = [P(*S(u, v)) for (u, v) in ret]
    return {'id': f'{kind}_{8 + idx}', 'name': sh['name'], 'kind': kind,
            'outline': [P(*p) for p in poly], 'pockets': [{'pos': P(*p)} for p in pockets],
            'obstacles': obs, 'layout': lay}
