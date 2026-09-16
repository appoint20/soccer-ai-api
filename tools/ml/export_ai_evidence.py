#!/usr/bin/env python3
"""Export football evidence from a quiescent local SQLite copy; never migrate or fetch AI."""
import argparse, datetime as dt, hashlib, json, pathlib, sqlite3
p=argparse.ArgumentParser();p.add_argument('database');p.add_argument('output');a=p.parse_args()
source=pathlib.Path(a.database).resolve();output=pathlib.Path(a.output).resolve()
if not source.is_file(): raise SystemExit('Source database does not exist.')
if output.exists() and any(output.iterdir()): raise SystemExit('Use an empty output directory to prevent mixing exports.')
wal=pathlib.Path(str(source)+'-wal')
if wal.exists() and wal.stat().st_size: raise SystemExit('Active WAL: provide a consistent backup.')
queries={'fixtures':'SELECT * FROM Fixtures ORDER BY Date, Id',
'ai-analyses':'''SELECT Id,FixtureId,Lang,HomeProb,DrawProb,AwayProb,Over25Prob,BttsProb,Goals23Prob,
AiOver25Qualified,AiBttsQualified,AiUnder25Qualified,AiGoals23Qualified,AiHomeWinQualified,AiAwayWinQualified,
AiOverallConfidence,CreatedAt,UpdatedAt FROM FixtureAnalyses WHERE AiOverallConfidence>0 ORDER BY FixtureId,Lang'''}
output.mkdir(parents=True,exist_ok=True);metadata={}
with sqlite3.connect(source.as_uri()+'?immutable=1',uri=True) as db:
 db.row_factory=sqlite3.Row
 tables={r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
 if 'PredictionSnapshots' in tables: queries['prediction-snapshots']='SELECT * FROM PredictionSnapshots ORDER BY Id'
 for name,query in queries.items():
  rows=[dict(r) for r in db.execute(query)]
  for row in rows:
   for key,val in row.items():
    if val is None: continue
    if isinstance(val,int) and (key=='Date' or key.endswith(('At','AtUtc','KickoffUtc','CapturedAtUtc'))):
     row[key]=(dt.datetime(1,1,1,tzinfo=dt.timezone.utc)+dt.timedelta(microseconds=val//10)).isoformat()
    if key.startswith('Is') or key.endswith(('Qualified','Pick')): row[key]=bool(val)
  data=json.dumps(rows,ensure_ascii=False,separators=(',',':')).encode();(output/(name+'.json')).write_bytes(data)
  metadata[name]={'Rows':len(rows),'Sha256':hashlib.sha256(data).hexdigest()}
(output/'export-provenance.json').write_text(json.dumps({'CapturedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat(),
 'Source':'local_sqlite_copy','ReadOnly':True,'Files':metadata},indent=2)+'\n')
print(json.dumps(metadata,indent=2))
