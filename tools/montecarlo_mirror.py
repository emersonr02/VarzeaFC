"""
Espelho do Varzea.Engine em Python — MESMA lógica do C#, usado só para validar
o modelo de balanceamento e scoring antes de rodar no .NET.
Não faz parte do produto: é banco de prova.
"""
import json, math, random, statistics, sys
from collections import defaultdict

BAL = json.load(open("Varzea.Engine/Ruleset/balance.json"))
ATTR_IDX = {"Pac":0,"Sho":1,"Pas":2,"Dri":3,"Def":4,"Phy":5,"Ski":6,"Wea":7}
POSITIONS = list(BAL["positionWeights"].keys())
TIERS = {t["tier"]: t for t in BAL["tiers"]}
CURVE = BAL["curve"]

AWARDS = {"BallonDOr", "TeamOfTheYear"}


def resolve_draft(rng, picks):
    pool = [dict(l) for l in BAL["legends"]]
    attrs = [0]*8
    for rnd in range(8):
        working = list(pool)
        cands = []
        for _ in range(min(3, len(working))):
            i = rng.randrange(len(working))
            cands.append(working.pop(i))
        chosen = cands[min(picks[rnd], len(cands)-1)]
        attrs[rnd] = chosen["ratings"][rnd]
        pool.remove(chosen)
    return attrs


def overall_for(attrs, pos):
    w = BAL["positionWeights"][pos]
    return round(sum(a*wi for a, wi in zip(attrs, w)))


def resolve_role(attrs, pos):
    for role in BAL["roles"][pos]:
        if attrs[ATTR_IDX[role["attr"]]] >= role["min"]:
            return role
    return {"name": "Indefinido"}


def out(rng, overall, factor, baseline, perf, apps, mod):
    base = overall/99.0 * baseline * factor
    v = base * (1+perf/70.0) * (apps/34.0) * (0.70+rng.random()*0.60) * (1+mod)
    return max(0, round(v))


def simulate(seed, country, picks, pos, transfers):
    rng = random.Random(seed)
    attrs = resolve_draft(random.Random(seed ^ 0xD1CE), picks)
    potential = overall_for(attrs, pos)
    role = resolve_role(attrs, pos)
    f = BAL["outputFactors"][pos]
    c = BAL["countries"][country]

    retire = rng.randint(CURVE["minRetireAge"], CURVE["maxRetireAge"])
    peak_age = rng.randint(CURVE["minPeakAge"], CURVE["maxPeakAge"])
    gap = rng.randint(CURVE["minPotentialGap"], CURVE["maxPotentialGap"])
    overall = max(40, min(potential-gap, max(41, potential-4)))

    if potential >= 90:   tier = 2 if rng.random() < .5 else 3
    elif potential >= 80: tier = 2
    elif potential >= 65: tier = 1 if rng.random() < .7 else 2
    else:                 tier = 1

    titles = defaultdict(int)
    peak = overall
    tot = {"goals":0,"assists":0,"tackles":0,"cs":0,"caps":0}
    seasons = 0
    ti = 0

    for age in range(CURVE["startAge"], retire+1):
        if age < peak_age:
            overall += max(1, round((potential-overall)*CURVE["growthRate"])) + (1 if rng.random()<.30 else 0)
        elif age <= peak_age+3:
            overall += rng.randint(-1, 2)
        else:
            overall -= rng.randint(1, 4)
        overall = max(35, min(99, overall))
        peak = max(peak, overall)

        target = TIERS[tier]["target"]
        perf = overall - target + (rng.random()*16-8)

        if age >= 30 and rng.random() < .006:
            break
        roll = rng.random()
        cost = 0
        if roll < .02:   cost = rng.randint(16,26)
        elif roll < .07: cost = rng.randint(8,16)
        elif roll < .17: cost = rng.randint(3,8)
        apps = max(4, min(38, rng.randint(24,36)-cost))

        sg = out(rng, overall, f["attack"],    30, perf, apps, role.get("goalsMod",0))
        sa = out(rng, overall, f["passing"],   22, perf, apps, role.get("assistsMod",0))
        st = out(rng, overall, f["defending"], 95, perf, apps, role.get("defenseMod",0))
        scs = round(apps * max(0, min(.60, .10 + f["defending"]*(.22 + perf/160.0))))

        tot["goals"] += sg; tot["assists"] += sa; tot["tackles"] += st; tot["cs"] += scs
        seasons += 1

        lpos = max(1, min(20, round(11 - perf/2.0 + (rng.random()*6-3))))
        season_titles = []

        if lpos == 1:
            kind = {3:"LeagueTop5",2:"LeagueMid"}.get(c["leagueGrade"], "LeagueMinor")
            season_titles.append(kind)

        if rng.random() < max(.03, min(.30, .10+perf/260.0)) and rng.random() < .40:
            season_titles.append("DomesticCup")

        cc = TIERS[tier]["continentalChance"]
        if cc > 0 and rng.random() < cc:
            if rng.random() < max(.10, min(.65, .35+perf/200.0)):
                season_titles.append("ContinentalPrimary" if tier >= 4 else "ContinentalSecondary")

        caps = 0
        if overall >= 76 and 18 <= age <= 35 and rng.random() < .35:
            caps += rng.randint(1,8)
        if (age-CURVE["startAge"]) % 4 == 2 and overall >= 74 and age >= 18:
            if rng.random() < max(.05, min(.60, (overall-70)/60.0 + c["strength"]/40.0)):
                caps += rng.randint(3,7)
                if rng.random() < max(.005, min(.18, .05+(overall-78)/260.0+(c["strength"]-5)/90.0)):
                    season_titles.append("WorldCup")
        tot["caps"] += caps

        # EQUIPE DO ANO: melhor de cada posição. A chance NÃO depende de gols nem
        # de desarmes — depende de quão acima do nível mundial você está na SUA posição.
        # Isso dá paridade entre posições por construção, sem prêmio setorial inventado.
        toty = max(0, min(.40, (overall-84)/38.0
                  + (.06 if lpos == 1 else 0)
                  + (.05 if "ContinentalPrimary" in season_titles else 0)
                  + (.04 if "WorldCup" in season_titles else 0)))
        if rng.random() < toty:
            season_titles.append("TeamOfTheYear")

        # Bola de Ouro: só quem entrou na Equipe do Ano concorre.
        # Sem isso o prêmio volta a ser refém de quem faz gol.
        if "TeamOfTheYear" in season_titles:
            wc = max(.02, min(.42, (overall-88)/50.0
                    + (.10 if lpos==1 else 0)
                    + (.14 if "ContinentalPrimary" in season_titles else 0)
                    + (.16 if "WorldCup" in season_titles else 0)
                    + role.get("titleMod",0)))
            if rng.random() < wc:
                season_titles.append("BallonDOr")

        for t in season_titles:
            titles[t] += 1

        if perf > 14 and tier < 5 and rng.random() < .45:
            if ti < len(transfers) and transfers[ti]: tier += 1
            ti += 1
        elif perf < -16 and tier > 1 and rng.random() < .30:
            if ti < len(transfers) and transfers[ti]: tier -= 1
            ti += 1

    return {"pos":pos, "potential":potential, "peak":peak, "seasons":seasons,
            "role":role["name"], "titles":dict(titles), **tot}


def raw_production(c):
    return (math.sqrt(c["goals"])*3.0 + math.sqrt(c["assists"])*2.2
            + math.sqrt(c["tackles"])*0.9 + math.sqrt(c["cs"])*1.6)


def calibrate(sample):
    kinds = ["LeagueTop5","LeagueMid","LeagueMinor","DomesticCup","ContinentalSecondary",
             "ContinentalPrimary","WorldCup","BallonDOr","TeamOfTheYear"]
    freq, weight = {}, {}
    for k in kinds:
        n = sum(1 for c in sample if c["titles"].get(k,0) > 0)
        fr = n/len(sample)
        freq[k] = fr
        weight[k] = math.log(1.0/max(0.0005, min(1.0, fr)))
    base = weight["LeagueMinor"]
    if base > 0:
        scale = 10.0/base
        weight = {k: round(v*scale, 2) for k, v in weight.items()}

    p50, p95 = {}, {}
    by_pos = defaultdict(list)
    for c in sample: by_pos[c["pos"]].append(raw_production(c))
    for p, vals in by_pos.items():
        vals.sort()
        p50[p] = vals[int(.50*(len(vals)-1))]
        p95[p] = vals[int(.95*(len(vals)-1))]
    return {"freq":freq, "weight":weight, "p50":p50, "p95":p95}


TITLE_CAP, AWARD_CAP, PROD_CAP, PEAK_CAP = 420, 300, 28, 7.6
TITLE_SCALE, AWARD_SCALE = 4.4, 5.0

def score(c, w):
    titles = awards = 0.0
    for k, n in c["titles"].items():
        v = w["weight"].get(k, 0)*n
        if k in AWARDS: awards += v
        else: titles += v
    titles = min(titles*TITLE_SCALE, TITLE_CAP)
    awards = min(awards*AWARD_SCALE, AWARD_CAP)
    p50 = w["p50"].get(c["pos"], 1); p95 = w["p95"].get(c["pos"], 2)
    raw = raw_production(c)
    norm = (raw-p50)/(p95-p50) if p95-p50 > 1e-4 else 0
    prod = max(0, min(2, norm+1))/3.0*PROD_CAP
    peak = max(0, min(1, (c["peak"]-60)/39.0))*PEAK_CAP
    return {"titles":titles, "awards":awards, "prod":prod, "peak":peak,
            "total":titles+awards+prod+peak}


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 10000
    countries = list(BAL["countries"].keys())
    sample = []
    for i in range(n):
        rng = random.Random(i*7919+13)
        sample.append(simulate(
            seed=i*104729+7,
            country=countries[rng.randrange(len(countries))],
            picks=[rng.randrange(3) for _ in range(8)],
            pos=POSITIONS[rng.randrange(len(POSITIONS))],
            transfers=[rng.random() < .8 for _ in range(12)]))

    w = calibrate(sample)
    scored = [(c, score(c, w)) for c in sample]

    print(f"Ruleset v{BAL['version']} · {n:,} carreiras\n")
    print("── PESOS DERIVADOS (não escritos à mão) ──")
    print(f"{'TÍTULO':<22}{'FREQUÊNCIA':>12}{'PESO':>9}")
    for k, v in sorted(w["weight"].items(), key=lambda x: -x[1]):
        print(f"{k:<22}{w['freq'][k]*100:>11.2f}%{v:>9.1f}")

    totals = sorted(s["total"] for _, s in scored)
    print(f"\n── CRITÉRIO 1: distribuição ──")
    print(f"mediana={totals[len(totals)//2]:.0f}  p99={totals[int(len(totals)*.99)]:.0f}  máx={totals[-1]:.0f}")
    top1 = sorted(scored, key=lambda x: -x[1]["total"])[:max(1, n//100)]
    distinct = len({round(s["total"], 2) for _, s in top1})
    ratio = distinct/len(top1)
    hi = top1[0][1]["total"]; lo = top1[-1][1]["total"]
    spread = (hi-lo)/hi if hi else 0
    print(f"top 1%: {distinct}/{len(top1)} scores distintos ({ratio:.0%}), "
          f"dispersão {spread:.0%} "
          + ("\u2714 OK" if ratio > .95 and spread > .15 else "\u2718 ACHATADO"))

    print(f"\n── CRITÉRIO 2: posições no top 10% ──")
    cut = sorted(scored, key=lambda x: -x[1]["total"])[:max(1, n//10)]
    ok2 = True
    for p in POSITIONS:
        it = sum(1 for c, _ in cut if c["pos"] == p)
        tt = sum(1 for c, _ in scored if c["pos"] == p)
        rate = it/tt if tt else 0
        if rate < .02: ok2 = False
        print(f"  {p:<4}{it:>5} no top 10%  ({rate*100:>5.1f}%) {'✘' if rate < .02 else ''}")
    print("✔ OK" if ok2 else "✘ posição sub-representada")

    print(f"\n── CRITÉRIO 3: contribuição por bloco ──")
    valid = [s for _, s in scored if s["total"] > 0]
    top10 = [s for _, s in sorted(scored, key=lambda x: -x[1]["total"])[:max(1, n//10)]]
    print(f"  {'BLOCO':<10}{'GERAL':>8}{'TOP 10%':>10}   alvo")
    for label, key, alvo in [("títulos","titles","~60%"),("prêmios","awards","~25%"),
                             ("produção","prod","~10%"),("pico","peak","~5%")]:
        m = statistics.mean(s[key]/s["total"] for s in valid)
        t = statistics.mean(s[key]/s["total"] for s in top10)
        print(f"  {label:<10}{m*100:>7.1f}%{t*100:>9.1f}%   {alvo}")
    print("  (prêmios raros zeram na maioria das carreiras — o TOP 10% é a leitura honesta)")


if __name__ == "__main__":
    main()
