#!/usr/bin/env python3
"""
SheNicest 数值节奏回归模拟器 v2.2（危机行为惩罚套件 M「萧条经济」定稿）
对应文档：《数值设计文档v2.2》
前提：P0 买地入账修复已落地——本模拟器按修复后口径建模（买地入账800，净增+480）

用法:
  python3 pace_sim_v22.py            # v2.2 定稿口径（M套件 + 胜利线110）
  python3 pace_sim_v22.py --legacy   # v2.1 旧口径对照（无M套件 + 胜利线100）

验收基线（5000局，±5%）:
  v2.2: 胜23.5 败45.8 平30.7 | 决胜率69% | 败局中位第6回合 | 时长6.2分 | 救援≈0.5
  v2.1: 胜36.5 败26.3 平37.3 | 时长6.9分 | 救援≈0.55

已知建模局限（详见文档附录B）:
  - 玩家0踩到他人地块仅约0.5次/局（棋盘22/54可购+产权分散），人格卡选卡公式对真人简化
    → 个体行为差异（声望轨迹/短板归属）以真机试玩为准，本模拟器验证的是总量分布
  - 破产拍卖按账面×60%建模（实机为市价×60%，危机区实机回血更少，方向安全）
"""
import random
import sys

# ===== v2.2 定稿参数（数值文档v2.2 第三/四节） =====
RECOMMENDED = dict(
    # ---- v2.1 沿用参数（本版不改） ----
    init_cash=1300, salary=300, base=400, step=400,
    buy_cost=320, up_cost=400, rent=0.16, l3=2.0,
    div=26, rounds=14, crisis_line=45, sprint_line=60,
    aid_mid=600, aid_crisis=700, neg_mid=0.60, neg_crisis=0.50,
    ev_rate_mid=0.40, ev_rate_sprint=0.55, robin=500, ev=750,
    # ---- v2.2 新增：胜利线 + M套件 ----
    win_line=110,          # 胜利线 100→110（胜利更稀有、更有含金量）
    fee=30, fee_low_rep=60,   # 危机维持费/回合（声望<45 恶名者加征翻倍）
    rep_line=45,             # 恶名线：低于此 = 民意抵制对象
    infl_rate=0.30, infl_cap=250,  # 通胀吞噬：超额现金每回合-30%，单回合上限250
    infl_th=600, infl_th_low=450,  # 通胀起征点（恶名者更低）
    rescue_rep_line=35,      # 救援门槛：慈善家不救声望<35的短板（众叛亲离）
    bonus=20,                # 冲刺繁荣红利/回合
)

# ===== Bot v2 性格槽（Bot性格槽设计文档 §二，本版不改） =====
PERSONAS = [
    dict(name='囤地豪客', safe_line=300, care=0.2, tough=0.75),
    dict(name='现金奶牛', safe_line=700, care=0.2, tough=0.90),
    dict(name='慈善家',   safe_line=500, care=0.9, tough=0.90),
    dict(name='均衡商人', safe_line=500, care=0.5, tough=0.80),
    dict(name='吸血鬼',   safe_line=400, care=0.1, tough=0.70),
]

def simulate(cfg, n=5000, legacy=False):
    out = {'win':0,'lose':0,'meh':0}; durs=[]; bp_all=0; ba_all=0; rescue_all=0
    pace_all=[]; peak_all=[]
    winl = 100 if legacy else cfg['win_line']
    for _ in range(n):
        rnd = random.Random()
        personas = [None] + rnd.sample(PERSONAS, 3)
        players = [{'cash':cfg['init_cash'],'pv':0,'rep':50,'si':rnd.randint(-30,30),
                    'pos':0,'alive':True} for _ in range(4)]
        tiles = [None]*54
        for p in rnd.sample(range(54),22): tiles[p] = {'owner':-2,'level':0}
        tv = lambda t_: cfg['base']+t_['level']*cfg['step']

        def rent_of(t_, pr):
            r = round(tv(t_)*(pr+50)/100*rnd.uniform(0.96,1.05)*cfg['rent'])
            if t_['level'] >= 3: r = round(r*cfg['l3'])
            return r

        def prosper():
            if not all(q['alive'] for q in players): return 0
            return round(min(q['cash']+q['pv'] for q in players)/cfg['div'])

        ADV=[-0.10,-0.10,0.30,0.30]; RATE=[0.20,-0.10,0.20,-0.20]
        def bargain(mp, mr, orr, ms, os_):
            card = lambda r,s: 3 if (r+s)/25>=3 else 2 if (r+s)/25>=2 else 1 if (r+s)/25>=1 else 0
            bc, sc = card(mr,ms), card(orr,os_)
            s = mp*(1+ADV[sc]+orr/200); b = mp*(1-ADV[bc]-mr/200)
            if b >= s: return round((s+b)/2)
            for r in (1,2,3):
                f = rnd.choice([0.3,0.5,0.7]) if r==1 else rnd.choice([0.4,0.6,0.8])
                s += (b-s)*(f+RATE[sc]); b += (s-b)*(f+RATE[bc])
                if b >= s: return round((s+b)/2)
            return None

        def bankruptcy(pi):
            p = players[pi]
            if p['cash'] >= 0 or not p['alive']: return
            while p['cash'] < 0:
                owned = [i for i,x in enumerate(tiles) if x and x['owner']==pi]
                if not owned: break
                tt = tiles[owned[0]]
                p['cash'] += round(tv(tt)*0.6); p['pv'] -= tv(tt)
                tt['owner'] = -2; tt['level'] = 0
            if p['cash'] < 0: p['cash'] = 0; p['pv'] = 0; p['alive'] = False

        t = 0.0; outcome = 'meh'; bp = ba = rescued = 0
        peak_pr = 50
        for ri in range(1, cfg['rounds']+1):
            for pi in range(4):
                p = players[pi]
                if not p['alive']: continue
                steps = max(rnd.randint(1,6), rnd.randint(1,6))
                op = p['pos']; p['pos'] = (p['pos']+steps) % 54
                pr0 = prosper()
                if pr0 > peak_pr: peak_pr = pr0
                if pr0 < cfg['crisis_line']:
                    zone='crisis'; salary, aid, neg, evr = 300, cfg['aid_crisis'], cfg['neg_crisis'], cfg['ev_rate_mid']
                elif pr0 >= cfg['sprint_line']:
                    zone='sprint'; salary, aid, neg, evr = 325, cfg['aid_mid'], cfg['neg_mid'], cfg['ev_rate_sprint']
                else:
                    zone='mid'; salary, aid, neg, evr = 300, cfg['aid_mid'], cfg['neg_mid'], cfg['ev_rate_mid']
                if p['pos'] < op: p['cash'] += salary
                t += 10 if pi==0 else 5

                # 人设（玩家0=均衡商人）
                ps = personas[pi] if pi != 0 else PERSONAS[3]
                safe_l, care, tough = ps['safe_line'], ps['care'], ps['tough']

                # 慈善家救援（v2.2 M5：声望<35 的短板不救——众叛亲离）
                if pi != 0 and care >= 0.9:
                    cands = [q for q in range(4) if q != pi and players[q]['alive']]
                    if cands:
                        poorest = min(cands, key=lambda q: players[q]['cash'])
                        if players[poorest]['cash'] < 300 and (legacy or players[poorest]['rep'] >= cfg['rescue_rep_line']):
                            owned = [i for i,x in enumerate(tiles) if x and x['owner']==poorest]
                            if owned and p['cash'] > safe_l:
                                tt = tiles[owned[0]]
                                price = round(tv(tt)*(pr0+50)/100*0.9)
                                if p['cash'] >= price:
                                    p['cash'] -= price; p['pv'] += tv(tt)
                                    players[poorest]['cash'] += price
                                    players[poorest]['pv'] -= tv(tt)
                                    tt['owner'] = pi; t += 5; rescued += 1

                tl = tiles[p['pos']]; pr = prosper()
                if tl is None:
                    if rnd.random() < evr:
                        r2 = rnd.random(); alive = [q for q in players if q['alive']]
                        if r2 < 0.6:
                            sign = -1 if rnd.random() < neg else 1
                            p['cash'] += sign*rnd.randint(100, cfg['ev'])
                        elif r2 < 0.8:
                            max(alive,key=lambda q: q['cash']+q['pv'])['cash'] -= cfg['robin']
                        else:
                            min(alive,key=lambda q: q['cash']+q['pv'])['cash'] += aid
                        t += 5
                    bankruptcy(pi); continue

                crisis_restraint = (zone == 'crisis' and care >= 0.7)
                ok = (p['cash'] >= safe_l) and not crisis_restraint
                if tl['owner'] == -2:
                    if p['cash'] >= max(300, cfg['buy_cost']) and ok:
                        p['cash'] -= cfg['buy_cost']; p['pv'] += tv({'level':1})
                        tl['level'] = 1; tl['owner'] = pi; t += 2
                elif tl['owner'] == pi:
                    if tl['level'] < 3 and p['cash'] >= cfg['up_cost']+safe_l and ok:
                        p['cash'] -= cfg['up_cost']; p['pv'] += cfg['step']
                        tl['level'] += 1; t += 2
                else:
                    o = players[tl['owner']]; rent = rent_of(tl, pr)
                    m = round(tv(tl)*(pr+50)/100); psych = round(m*tough)
                    if pi == 0 and not legacy:
                        # 真人玩家：砍价面板随时可点（对齐实机，无心理价负担门槛）
                        _gate = p['cash'] >= 300; _prob = 1.0
                    else:
                        _gate = p['cash'] >= max(300, psych); _prob = 0.85
                    if _gate and rnd.random() < _prob:
                        price = bargain(m, p['rep'], o['rep'], p['si'], o['si'])
                        if price is not None and p['cash'] >= price:
                            p['cash'] -= price; p['pv'] += tv(tl)
                            o['cash'] += price; tl['owner'] = pi
                            if not legacy:
                                # 人格卡声望传感器（对齐实机 BargainState.reputationChange）:
                                # 压价成交(<=市价9折) -3 / 实价 +3 / 让利(>=市价) +5
                                p['rep'] = max(0, min(100, p['rep'] + (-3 if price < 0.9*m else (5 if price >= m else 3))))
                        if pi == 0: bp += 1; t += 45
                        else: ba += 1; t += 20
                    else:
                        p['cash'] -= rent; o['cash'] += rent
                bankruptcy(pi)

            # ===== v2.2 M套件：回合末按区间收发（数值文档v2.2 第四节） =====
            if not legacy:
                prx = prosper()
                if prx < cfg['crisis_line']:
                    # 危机：维持费（恶名者翻倍）+ 通胀吞噬（囤现金被吃，蒸发不转移）
                    for q in players:
                        if not q['alive']: continue
                        low = q['rep'] < cfg['rep_line']
                        q['cash'] -= (cfg['fee_low_rep'] if low else cfg['fee'])
                        th = cfg['infl_th_low'] if low else cfg['infl_th']
                        ex = q['cash'] - th
                        if ex > 0:
                            q['cash'] -= min(round(ex*cfg['infl_rate']), cfg['infl_cap'])
                elif prx >= cfg['sprint_line']:
                    # 冲刺：繁荣红利
                    for q in players:
                        if q['alive']: q['cash'] += cfg['bonus']

            pr = prosper()
            if pr > peak_pr: peak_pr = pr
            if pr >= winl: outcome='win'; break
            if not all(q['alive'] for q in players) or pr <= 20: outcome='lose'; break
        end_r = ri if outcome != 'meh' else cfg['rounds']
        out[outcome] += 1; durs.append(t/60)
        pace_all.append((outcome, end_r))
        peak_all.append(peak_pr)
        bp_all += bp; ba_all += ba; rescue_all += rescued
    N = n
    w,l,m = out['win']/N, out['lose']/N, out['meh']/N
    dec = w+l
    wr = sorted(e[1] for e in pace_all if e[0]=='win'); lr = sorted(e[1] for e in pace_all if e[0]=='lose')
    med = lambda a: a[len(a)//2] if a else 0
    b_ge = sum(1 for x in peak_all if x >= winl)
    b_near = sum(1 for x in peak_all if (winl-30) <= x < winl)
    tag = 'v2.1 旧口径（对照）' if legacy else 'v2.2 定稿口径（M套件）'
    print(f'{tag}回归（{N}局·性格槽洗牌）:')
    print(f'  胜{w*100:.1f}% 败{l*100:.1f}% 平{m*100:.1f}% | 决胜率{dec*100:.0f}% | 时长{sum(durs)/N:.1f}分')
    print(f'  胜局中位第{med(wr)}回合 | 败局中位第{med(lr)}回合 | bargain: 玩家{bp_all/N:.2f} AI互谈{ba_all/N:.2f} | 慈善家救援{rescue_all/N:.2f}次/局')
    print(f'  峰值繁荣: 登顶≥{winl}: {b_ge/N*100:.0f}% | 临门一脚{winl-30}~{winl-1}: {b_near/N*100:.0f}%')
    if legacy:
        print('  验收基线(v2.1): 胜36.5 败26.3 平37.3 ±5% | 救援≈0.55 | 时长5-8分')
    else:
        print('  验收基线(v2.2): 胜23.5 败45.8 平30.7 ±5% | 败局中位第6回合 | 时长6.2分 | 救援≈0.5')

if __name__ == '__main__':
    simulate(RECOMMENDED, legacy=('--legacy' in sys.argv))
