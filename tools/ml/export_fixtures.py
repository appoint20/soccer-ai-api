#!/usr/bin/env python3
"""Read a quiescent SQLite football snapshot without modifying it; export only fixture columns."""
import argparse, datetime as dt, hashlib, json, pathlib, sqlite3
p=argparse.ArgumentParser(); p.add_argument('database');p.add_argument('output');a=p.parse_args()
src=pathlib.Path(a.database).resolve();out=pathlib.Path(a.output).resolve()
wal=pathlib.Path(str(src)+'-wal')
if wal.exists() and wal.stat().st_size: raise SystemExit('Active WAL: create a consistent backup before exporting.')
with sqlite3.connect(f'file:{src}?immutable=1',uri=True) as db:
 db.row_factory=sqlite3.Row
 rows=[dict(r) for r in db.execute('SELECT * FROM Fixtures ORDER BY Date, Id')]
for row in rows:
 for key,value in row.items():
  if value is None: continue
  if key=='Date' or key.endswith('At') or key.endswith('AtUtc'):
   if isinstance(value,int): row[key]=(dt.datetime(1,1,1,tzinfo=dt.timezone.utc)+dt.timedelta(microseconds=value//10)).isoformat()
  if key.startswith('Is'): row[key]=bool(value)
out.parent.mkdir(parents=True,exist_ok=True);data=json.dumps(rows,ensure_ascii=False,separators=(',',':')).encode();out.write_bytes(data)
metadata={'database':str(src),'fixtures':len(rows),'finished':sum(r['Status']=='FT' for r in rows),'sha256':hashlib.sha256(data).hexdigest(),'exported_at_utc':dt.datetime.now(dt.timezone.utc).isoformat(),'earliest':min(r['Date'] for r in rows),'latest':max(r['Date'] for r in rows)}
out.with_suffix('.provenance.json').write_text(json.dumps(metadata,indent=2)+'\n');print(json.dumps(metadata,indent=2))
