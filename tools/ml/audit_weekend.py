#!/usr/bin/env python3
"""Offline, predefined Sunday test. No provider calls, DB writes, or automatic model changes."""
import argparse, bisect, collections, datetime as dt, hashlib, json, math, random
from pathlib import Path
from zoneinfo import ZoneInfo

def fit(rows, market, with_trigger):
    a = b = 0.0
    for _ in range(40):
        ga = gb = aa = ab = bb = 0.0
        for r in rows:
            p0, y, x = r[market]
            z = math.log(p0/(1-p0)) + a + (b*x if with_trigger else 0)
            p = 1/(1+math.exp(-max(-20,min(20,z))))
            w = p*(1-p); ga += y-p; aa += w
            if with_trigger: gb += (y-p)*x; ab += w*x; bb += w*x*x
        if with_trigger:
            # A fixed, weak ridge penalty avoids a singular/separated fit.
            ga -= a; gb -= b; aa += 1; bb += 1
            det = aa*bb-ab*ab
            da, db = (ga*bb-gb*ab)/det, (gb*aa-ga*ab)/det
        else: da, db = (ga-a)/(aa+1), 0
        a += da; b += db
        if abs(da)+abs(db)<1e-8: break
    return a,b

def score(r, market, params):
    p,y,x=r[market]; a,b=params
    q=1/(1+math.exp(-(math.log(p/(1-p))+a+b*x)))
    return (q-y)**2, -(y*math.log(q)+(1-y)*math.log(1-q)),q

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--input',required=True);ap.add_argument('--output',required=True)
    args=ap.parse_args(); raw=Path(args.input).read_bytes(); source=json.loads(raw)
    tz=ZoneInfo('Europe/Berlin'); cutoff=dt.date(2025,7,1)
    fixtures=[]
    for f in source:
        if f['Status']!='FT': continue
        date=dt.datetime.fromisoformat(f['Date'].replace('Z','+00:00')).astimezone(tz)
        fixtures.append((date,f['LeagueId'],int(f['HomeGoal']+f['AwayGoal']>2),int(f['HomeGoal']>0 and f['AwayGoal']>0)))
    fixtures.sort(); history=collections.defaultdict(list); weekends=collections.defaultdict(list)
    for f in fixtures:
        history[f[1]].append(f)
        friday=f[0].date()-dt.timedelta(days=(f[0].weekday()-4)%7)
        if f[0].weekday() in [4,5,6]: weekends[(f[1],friday)].append(f)
    rows=[]
    for (league,friday), games in sorted(weekends.items(),key=lambda x:x[0][1]):
        previous=[g for g in history[league] if g[0].date()<friday]
        if len(previous)<50: continue
        # Prior league frequency known before Friday; fixed 20-observation 50% shrinkage.
        base=[(sum(g[i] for g in previous)+10)/(len(previous)+20) for i in [2,3]]
        for game in games:
            if game[0].weekday()!=6: continue
            early=[g for g in games if g[0].weekday() in [4,5] and g[0]+dt.timedelta(hours=3)<=game[0]]
            if len(early)<3: continue
            r={'week':str(friday),'date':game[0].date(),'league':league}
            for i,m in enumerate(['over25','btts']):
                r[m]=(base[i],game[i+2],int(sum(g[i+2] for g in early)/len(early)<.5))
            rows.append(r)
    train=[r for r in rows if r['date']<cutoff];test=[r for r in rows if r['date']>=cutoff]
    report={'dataset_sha256':hashlib.sha256(raw).hexdigest(),'split_date':str(cutoff),
        'protocol':'Europe/Berlin calendar; Sunday targets, >=3 completed Friday/Saturday games in same league; trigger: fewer than half were Over25/GG; fixed 3h completion allowance. League prior uses only matches before Friday, >=50 history; 20-observation 50% shrinkage. Train an intercept-only logistic offset baseline and intercept+trigger before split; freeze coefficients for later test. No betting odds filter.',
        'train_matches':len(train),'test_matches':len(test),'markets':{}}
    for m in ['over25','btts']:
        baseline=fit(train,m,False); pattern=fit(train,m,True)
        triggered=[r for r in test if r[m][2]]
        def metrics(data,model):
            scores=[score(r,m,model) for r in data]; n=len(scores)
            return {'n':n,'brier':sum(x[0] for x in scores)/n,'log_loss':sum(x[1] for x in scores)/n,'mean_probability':sum(x[2] for x in scores)/n} if n else {'n':0}
        blocks=collections.defaultdict(list)
        for r in test: blocks[r['week']].append(score(r,m,pattern)[0]-score(r,m,baseline)[0])
        values=list(blocks.values());rng=random.Random(20260909);boot=[]
        for _ in range(2000):
            chosen=[values[rng.randrange(len(values))] for _ in values]
            boot.append(sum(map(sum,chosen))/sum(map(len,chosen)))
        boot.sort()
        report['markets'][m]={'train_trigger_log_odds_coefficient':pattern[1],
            'triggered_test_matches':len(triggered),'triggered_test_hit_rate':sum(r[m][1] for r in triggered)/len(triggered) if triggered else None,
            'baseline':metrics(test,baseline),'with_weekend_trigger':metrics(test,pattern),
            'triggered_baseline':metrics(triggered,baseline),'triggered_pattern':metrics(triggered,pattern),
            'test_brier_difference_pattern_minus_baseline_95_week_cluster_bootstrap':[boot[49],boot[1949]]}
    report['limitations']='Observational diagnostic, not causal proof; incomplete league/weekend coverage possible, no lineup/odds or fixture difficulty matching. Chronological split chosen before this run, but not preregistered externally. Intervals are exploratory and unadjusted for two markets. A weekend need not contain all league fixtures or exactly 11 games.'
    out=Path(args.output);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))

if __name__=='__main__': main()
