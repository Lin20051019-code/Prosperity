#!/usr/bin/env python3
"""
像素化处理管线 v2 —— SheNicest 项目美术素材批处理工具
（2026-09-19 重写。v1 脚本（/tmp/bargain_expr/）随重启丢失，本版固化进 tools/ 供长期复用。）

用法:
  python3 tools/pixelate_asset.py <输入图> <输出图> --mode <keep|cropband|blackalpha> [--block 4] [--colors 32] [--cutoff 20]
  python3 tools/pixelate_asset.py vignette <输出图> [--block 2] [--colors 16] [--size 1280x720] [--max-alpha 210]
  python3 tools/pixelate_asset.py <输入图> <输出前缀> --mode blackalpha --slice  (光点图集:按连通域切子图)

模式:
  keep       仅像素化（面板底图类：先小图量化再放大，块状复古感）
  cropband   从纯黑背景自动裁出主体横带 + 近黑转透明 + 像素化（卷轴/横幅类）
  blackalpha 亮度转alpha：黑色背景变透明、亮度越高越不透明（光束/光点类）
  vignette   纯代码生成径向暗角（无需输入图；程序端用 Image.color 可再tint冷/暖）
  --slice    配合 blackalpha：把不透明像素按连通域切成多个独立小图（光点图集）

风格基准（与角色表情管线一致）:
  block=4（约4px像素块，2560宽素材≈640逻辑宽）、colors=24~32（暖棕色系面板）/ 16（暗角）
"""
import argparse
import os
import sys

from PIL import Image


def pixelate(img, block, colors):
    """整数块降采样 → 调色板量化 → 最近邻放大回去"""
    w, h = img.size
    small = img.resize((max(1, w // block), max(1, h // block)), Image.NEAREST)
    small = small.convert('RGB').quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.NONE).convert('RGB')
    return small.resize((small.width * block, small.height * block), Image.NEAREST)


def crop_band_from_black(im_rgb, cutoff=20, pad=6):
    """从黑底图裁出主体（按"非黑像素"包围盒），并把近黑区域转透明"""
    gray = im_rgb.convert('L')
    w, h = gray.size
    px = gray.load()
    top, bottom, left, right = h, 0, w, 0
    for y in range(h):
        row_hit = False
        for x in range(0, w, 4):  # 隔4采样加速
            if px[x, y] > cutoff + 18:
                row_hit = True
                break
        if row_hit:
            top = min(top, y); bottom = max(bottom, y)
    if bottom <= top:
        return None
    for x in range(w):
        col_hit = False
        for y in range(top, bottom + 1, 4):
            if px[x, y] > cutoff + 18:
                col_hit = True
                break
        if col_hit:
            left = min(left, x); right = max(right, x)
    if right <= left:
        return None
    top = max(0, top - pad); bottom = min(h - 1, bottom + pad)
    left = max(0, left - pad); right = min(w - 1, right + pad)
    return im_rgb.crop((left, top, right + 1, bottom + 1))


def black_to_alpha(im_rgb, cutoff=24):
    """黑色→透明：alpha=max(0,亮度-cutoff)，颜色保留"""
    gray = im_rgb.convert('L')
    rgba = im_rgb.convert('RGBA')
    g_px, a_px = gray.load(), rgba.load()
    w, h = gray.size
    for y in range(h):
        for x in range(w):
            a = g_px[x, y]
            alpha = 0 if a <= cutoff else min(255, int((a - cutoff) * 255 / (255 - cutoff)))
            r, g, b, _ = a_px[x, y]
            a_px[x, y] = (r, g, b, alpha)
    return rgba


def near_black_to_transparent(rgba, cutoff=18):
    """近黑像素清透明（卷轴裁切后清残留黑角）"""
    px = rgba.load()
    w, h = rgba.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if r <= cutoff and g <= cutoff and b <= cutoff:
                px[x, y] = (r, g, b, 0)
    return rgba


def halo_cleanup(rgba, alpha_hi=110, bright_hi=110):
    """v1管线第④步：近透明区的亮色杂散清零（白边/噪点治理）"""
    px = rgba.load()
    w, h = rgba.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if 0 < a <= alpha_hi and max(r, g, b) >= bright_hi:
                px[x, y] = (r, g, b, 0)
    return rgba


def quantize_alpha(rgba, steps):
    """alpha 阶梯量化：渐变变像素硬边带（像素风正字标）"""
    if steps <= 1:
        return rgba
    px = rgba.load()
    w, h = rgba.size
    levels = [round(i * 255 / (steps - 1)) for i in range(steps)]
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            best = min(levels, key=lambda v: abs(v - a))
            px[x, y] = (r, g, b, best)
    return rgba


def keep_largest_component(rgba, alpha_min=30):
    """只保留最大连通域（去孤立噪点碎片）"""
    px = rgba.load()
    w, h = rgba.size
    labels = [[0] * w for _ in range(h)]
    best_id, best_area, cur_id = 0, 0, 0
    for sy in range(h):
        for sx in range(w):
            if labels[sy][sx] or px[sx, sy][3] < alpha_min:
                continue
            cur_id += 1
            stack = [(sx, sy)]
            labels[sy][sx] = cur_id
            area = 0
            while stack:
                x, y = stack.pop()
                area += 1
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if 0 <= nx < w and 0 <= ny < h and not labels[ny][nx] and px[nx, ny][3] >= alpha_min:
                        labels[ny][nx] = cur_id
                        stack.append((nx, ny))
            if area > best_area:
                best_area, best_id = area, cur_id
    for y in range(h):
        for x in range(w):
            if labels[y][x] and labels[y][x] != best_id:
                r, g, b, _ = px[x, y]
                px[x, y] = (r, g, b, 0)
    return rgba


def edge_polish(rgba, block=2, inset=3, min_hole=6):
    """收边精修：纯内缩去毛刺 + 洪泛法识别"内部真孔"并扩散填色。
    不用闭运算（避免把卷边/凹腔当孔过度膨胀）。轴帽卷边的自然不规则轮廓会被保留。"""
    from PIL import ImageChops, ImageFilter
    w, h = rgba.size
    sw, sh = max(1, w // block), max(1, h // block)
    small = rgba.resize((sw, sh), Image.NEAREST)
    rgb = small.convert('RGB')
    mask = small.split()[3].point(lambda v: 255 if v >= 64 else 0)
    for _ in range(inset):
        mask = mask.filter(ImageFilter.MinFilter(3))          # 内缩：削孤立毛刺/锯齿
    # 洪泛：从图像边界走"非mask"区域 = 外部；走不到的非mask = 内部真孔
    mpx = mask.load()
    exterior = [[False] * sw for _ in range(sh)]
    stack = []
    for x in range(sw):
        for y in (0, sh - 1):
            if mpx[x, y] == 0 and not exterior[y][x]:
                exterior[y][x] = True; stack.append((x, y))
    for y in range(sh):
        for x in (0, sw - 1):
            if mpx[x, y] == 0 and not exterior[y][x]:
                exterior[y][x] = True; stack.append((x, y))
    while stack:
        x, y = stack.pop()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < sw and 0 <= ny < sh and not exterior[ny][nx] and mpx[nx, ny] == 0:
                exterior[ny][nx] = True; stack.append((nx, ny))
    # 孔合并 + 面积过滤（小针孔忽略）
    hole_mask = Image.new('L', (sw, sh), 0)
    hpx = hole_mask.load()
    visited = [[False] * sw for _ in range(sh)]
    for y in range(sh):
        for x in range(sw):
            if mpx[x, y] == 0 and not exterior[y][x] and not visited[y][x]:
                comp = [(x, y)]; visited[y][x] = True; st = [(x, y)]
                while st:
                    cx, cy = st.pop()
                    for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                        if 0 <= nx < sw and 0 <= ny < sh and mpx[nx, ny] == 0 and not exterior[ny][nx] and not visited[ny][nx]:
                            visited[ny][nx] = True; st.append((nx, ny)); comp.append((nx, ny))
                if len(comp) >= min_hole:
                    for (hx, hy) in comp:
                        hpx[hx, hy] = 255
    # 邻域亮色向孔内扩散填色
    blacked = Image.composite(rgb, Image.new('RGB', (sw, sh), (0, 0, 0)), mask)
    spread = blacked.filter(ImageFilter.MaxFilter(min_hole * 2 + 1))
    filled = Image.composite(spread, rgb, hole_mask)
    out = filled.convert('RGBA')
    out.putalpha(ImageChops.lighter(mask, hole_mask))
    return out.resize((w, h), Image.NEAREST)


def slice_components(rgba, min_area=40, max_count=8, keep_ratio=0.15):
    """按连通域（4邻接BFS）切出独立元素；过滤面积小于最大元素15%的碎片"""
    px = rgba.load()
    w, h = rgba.size
    seen = [[False] * w for _ in range(h)]
    boxes = []
    for sy in range(h):
        for sx in range(w):
            if seen[sy][sx] or px[sx, sy][3] < 40:
                continue
            stack = [(sx, sy)]
            seen[sy][sx] = True
            minx = maxx = sx; miny = maxy = sy; area = 0
            while stack:
                x, y = stack.pop()
                area += 1
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
                for nx, ny in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and px[nx, ny][3] >= 40:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            boxes.append((minx, miny, maxx, maxy, area))
    if not boxes:
        return []
    largest = max(b[4] for b in boxes)
    boxes = [b for b in boxes if b[4] >= max(min_area, largest * keep_ratio)]
    boxes.sort(key=lambda b: -b[4])
    boxes = boxes[:max_count]
    boxes.sort(key=lambda b: (b[1], b[0]))  # 按位置排序：从左上到右下
    out = []
    for (minx, miny, maxx, maxy, _) in boxes:
        out.append(rgba.crop((minx, miny, maxx + 1, maxy + 1)))
    return out


def make_vignette(size=(1280, 720), max_alpha=210, r0_ratio=0.52, r1_ratio=1.18):
    """径向暗角：中心透明、四角最暗。中性蓝灰色，程序端可 tint"""
    w, h = size
    cx, cy = w / 2, h * 0.5
    corner = (cx ** 2 + cy ** 2) ** 0.5
    r0, r1 = corner * r0_ratio, corner * r1_ratio
    im = Image.new('RGBA', size)
    px = im.load()
    for y in range(h):
        for x in range(w):
            r = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            if r <= r0:
                a = 0
            elif r >= r1:
                a = max_alpha
            else:
                t = (r - r0) / (r1 - r0)
                t = t * t * (3 - 2 * t)  # smoothstep
                a = int(max_alpha * t)
            px[x, y] = (15, 20, 30, a)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('input', help='输入图路径；mode=vignette 时忽略')
    ap.add_argument('output', help='输出路径（--slice 时为输出文件名前缀）')
    ap.add_argument('--mode', default='keep', choices=['keep', 'cropband', 'blackalpha', 'vignette'])
    ap.add_argument('--block', type=int, default=4)
    ap.add_argument('--colors', type=int, default=32)
    ap.add_argument('--cutoff', type=int, default=24)
    ap.add_argument('--alpha-steps', type=int, default=0, help='alpha阶梯量化级数（0=关；光束类建议4）')
    ap.add_argument('--slice', action='store_true', help='blackalpha 后按连通域切子图')
    args = ap.parse_args()

    if args.mode == 'vignette':
        out = make_vignette()
        out = pixelate(out, args.block, args.colors)
        out.save(args.output)
        print(f'vignette -> {args.output} {out.size}')
        return

    im = Image.open(args.input).convert('RGB')
    if args.mode == 'cropband':
        band = crop_band_from_black(im, cutoff=args.cutoff)
        if band is None:
            print('!! 未检测到主体横带', file=sys.stderr); sys.exit(1)
        rgba = band.convert('RGBA')
        rgba = near_black_to_transparent(rgba, cutoff=max(30, args.cutoff))
        rgba = halo_cleanup(rgba)
        rgba = keep_largest_component(rgba)
        rgba = edge_polish(rgba, block=2, inset=3)
        rgba = keep_largest_component(rgba)
        out = pixelate(rgba.convert('RGB'), args.block, args.colors)
        out = out.convert('RGBA')
        # 像素化后的透明掩膜同样块化，避免半透明糊边
        small_a = rgba.resize((max(1, rgba.width // args.block), max(1, rgba.height // args.block)), Image.NEAREST)
        small_a = small_a.resize((small_a.width * args.block, small_a.height * args.block), Image.NEAREST)
        out.putalpha(small_a.split()[3])
        out.save(args.output)
        print(f'cropband -> {args.output} {out.size}')
        return

    if args.mode == 'blackalpha':
        rgba = black_to_alpha(im, cutoff=args.cutoff)
        rgba = halo_cleanup(rgba)
        if args.alpha_steps > 1:
            rgba = quantize_alpha(rgba, args.alpha_steps)
        out = pixelate(rgba.convert('RGB'), args.block, args.colors).convert('RGBA')
        # 块化alpha（与颜色同网格，保持像素锐利）
        small = rgba.resize((max(1, rgba.width // args.block), max(1, rgba.height // args.block)), Image.NEAREST)
        small = small.resize((small.width * args.block, small.height * args.block), Image.NEAREST)
        out.putalpha(small.split()[3])
        if args.slice:
            pieces = slice_components(out)
            base, ext = os.path.splitext(args.output)
            for i, p in enumerate(pieces, 1):
                path = f'{base}_{i}{ext}'
                p.save(path)
                print(f'slice[{i}] -> {path} {p.size}')
            print(f'共切出 {len(pieces)} 个元素')
        else:
            out = keep_largest_component(out)
            out.save(args.output)
            print(f'blackalpha -> {args.output} {out.size}')
        return

    # keep
    out = pixelate(im, args.block, args.colors)
    out.save(args.output)
    print(f'keep -> {args.output} {out.size}')


if __name__ == '__main__':
    main()
