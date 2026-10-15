"""Phase 3 end-to-end checks (HST models, response analysis, DHI, documents, availability).
Run AFTER integration_test.py and integration_test_phase2.py on the same database."""
import os, sys, math, random, datetime as dt, requests
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
adm=login('admin','Admin@2026x'); dse=login('arjun','Temp@2026'); vik=login('vikram','Temp@2026'); ins=login('meera','Temp@2026')
# ---------- synthetic 2-year dataset with a known HST behaviour ----------
random.seed(42); IST=dt.timezone(dt.timedelta(hours=5,minutes=30))
end=dt.datetime(2026,10,1,0,0,tzinfo=dt.timezone.utc); N=730*4
times=[end-dt.timedelta(hours=6*(N-1-k)) for k in range(N)]
rwl=[650+10*math.sin(2*math.pi*t.timetuple().tm_yday/365.25-1.2)+0.05*random.gauss(0,1) for t in times]
hmin,hmax=min(rwl),max(rwl); t0=times[0]
def truth(t,h):
  z=(h-hmin)/(hmax-hmin); s=2*math.pi*t.timetuple().tm_yday/365.25; tau=(t-t0).total_seconds()/86400/365.25
  return 120+25*z+8*z*z+4*math.sin(s)-3*math.cos(s)+1.5*tau
pz=[truth(t,h)+0.4*random.gauss(0,1) for t,h in zip(times,rwl)]
rain=[max(0,random.gauss(0,1)*15-12) for _ in times]
iso=lambda t: t.astimezone(IST).strftime('%Y-%m-%dT%H:%M:00+05:30')
payload={'fileName':'synthetic_2y.json','commit':True,
 'dams':[{'damId':'DM-900','damName':'Synthetic Test Dam','state':'Test','damType':'Concrete gravity','hazardClass':'Low','heightM':40,'frlM':660,'mwlM':662,'mddlM':635,'crestLevelM':663}],
 'sensors':[{'sensorId':'DM900-RL-01','damId':'DM-900','parameterCode':'RWL','engUnit':'m','expectedMin':600,'expectedMax':670,'direction':'Rising','readingIntervalMin':360},
            {'sensorId':'DM900-PZ-01','damId':'DM-900','parameterCode':'UPL','engUnit':'kPa','expectedMin':0,'expectedMax':400,'direction':'Rising','readingIntervalMin':360},
            {'sensorId':'DM900-RG-01','damId':'DM-900','parameterCode':'RNF','engUnit':'mm','expectedMin':0,'expectedMax':400,'direction':'Rising','readingIntervalMin':360}],
 'readings':[r for t,h,p,rn in zip(times,rwl,pz,rain) for r in (
   {'damId':'DM-900','sensorId':'DM900-RL-01','parameterCode':'RWL','observedAt':iso(t),'value':round(h,3),'unit':'m'},
   {'damId':'DM-900','sensorId':'DM900-PZ-01','parameterCode':'UPL','observedAt':iso(t),'value':round(p,3),'unit':'kPa'},
   {'damId':'DM-900','sensorId':'DM900-RG-01','parameterCode':'RNF','observedAt':iso(t),'value':round(rn,1),'unit':'mm'})]}
r=vik.post(B+'/import',json=payload).json(); ok(r['committed'] and r['good']+r['suspect']>=3*N-20,f"synthetic 2-year dataset imported ({r['good']} G, {r['suspect']} S)")
dam=[d for d in dse.get(B+'/dams').json() if d['code']=='DM-900'][0]; D=dam['id']
sens={s['code']:s['id'] for s in dse.get(B+f'/dams/{D}/sensors').json()}; PZ=sens['DM900-PZ-01']
# ---------- HST ----------
ok(vik.post(B+f"/sensors/{sens['DM900-RL-01']}/hst/fit",json={}).status_code==400,'HST refused for a load sensor (reservoir level)')
ok(ins.post(B+f'/sensors/{PZ}/hst/fit',json={}).status_code==403,'inspector cannot fit models')
m=vik.post(B+f'/sensors/{PZ}/hst/fit',json={'kLimit':3}).json()
ok(m['variant']=='Full HST' and m['nPoints']>=N-10,f"full HST fitted on {m['nPoints']} points")
ok(m['r2']>0.97,f"R² = {m['r2']:.4f} (> 0.97)")
ok(0.3<m['sigma']<0.55,f"residual σ = {m['sigma']:.3f} ≈ simulated noise 0.4")
ok(1.0<m['driftPerYear']<2.0,f"irreversible drift recovered: {m['driftPerYear']:.2f} kPa/year (true 1.5)")
g=dse.get(B+f'/sensors/{PZ}/hst').json(); err=max(abs(x['predicted']-truth(dt.datetime.fromisoformat(x['t'].replace('Z','+00:00')),x['rwl'])) for x in g['series'])
ok(err<0.5,f"prediction vs true behaviour: max error {err:.3f} kPa over the last year")
ok(vik.post(B+f"/sensors/{PZ}/hst/{m['id']}/approve").status_code==403,'instrumentation engineer cannot approve models')
ok(dse.post(B+f"/sensors/{PZ}/hst/{m['id']}/approve").status_code==204,'dam safety engineer approves model (not the fitter)')
ok(dse.get(B+f'/sensors/{PZ}/hst').json()['shown']['status']=='Active','model is Active')
# anomaly: two readings 10σ above prediction → HST alert
t1=end+dt.timedelta(hours=6); t2=end+dt.timedelta(hours=12); hh=rwl[-1]
anom={'fileName':'anomaly','commit':True,'readings':[
 {'damId':'DM-900','sensorId':'DM900-RL-01','parameterCode':'RWL','observedAt':iso(t1),'value':round(hh,3),'unit':'m'},{'damId':'DM-900','sensorId':'DM900-RL-01','parameterCode':'RWL','observedAt':iso(t2),'value':round(hh,3),'unit':'m'},
 {'damId':'DM-900','sensorId':'DM900-PZ-01','parameterCode':'UPL','observedAt':iso(t1),'value':round(truth(t1,hh)+4.5,3),'unit':'kPa'},
 {'damId':'DM-900','sensorId':'DM900-PZ-01','parameterCode':'UPL','observedAt':iso(t2),'value':round(truth(t2,hh)+4.6,3),'unit':'kPa'}]}
res=vik.post(B+'/import',json=anom).json()
al=[a for a in dse.get(B+f'/alerts?damId={D}').json() if a['ruleType']=='HST']
ok(len(al)==1 and al[0]['level']==1,'reading 4.5 kPa above the HST prediction (≈ 11σ) twice → Level 1 HST alert')
ok(not [a for a in dse.get(B+f'/alerts?damId={D}').json() if a['ruleType']=='Statistical' and a.get('sensorCode')=='DM900-PZ-01'],'statistical rule replaced by the approved HST model')
# provisional model on 90-day data
pz7=[s for s in dse.get(B+'/dams').json() if s['code']=='DM-001'][0]['id']; s7={s['code']:s['id'] for s in dse.get(B+f'/dams/{pz7}/sensors').json()}
p=vik.post(B+f"/sensors/{s7['DM001-PZ-07']}/hst/fit",json={}).json(); ok(p['variant']=='Provisional H+T' and 'seasonal' in p['notes'],'90 days of data → provisional H+T model with a warning')
# ---------- response analysis ----------
rr=dse.get(B+f'/sensors/{PZ}/response').json()
ok(rr['reservoir']['bestR']>0.8 and rr['reservoir']['bestLagHours']==0,f"reservoir response: r = {rr['reservoir']['bestR']}, lag {rr['reservoir']['bestLagHours']} h")
ok(rr['rain'] is not None and len(rr['rain']['events'])>0,f"rain response computed ({len(rr['rain']['events'])} events ≥ 20 mm/day)")
ok(abs(rr['rain']['bestR'] or 0)<0.3,'no rainfall effect detected (none was simulated)')
# ---------- documents & DHI ----------
ok(ins.post(B+f'/dams/{pz7}/documents',json={'docType':'EAP','title':'x','status':'Approved'}).status_code==403,'inspector cannot register documents')
ok(dse.post(B+f'/dams/{pz7}/documents',json={'docType':'Leaflet','title':'x','status':'Approved'}).status_code==400,'unknown document type rejected')
before=dse.get(B+f'/dams/{pz7}/dhi').json()['current']
doc=lambda t: [c for c in t['components'] if c['code']=='DOCUMENTATION'][0]['score']
for t,ti,iss,rev in [('EAP','Emergency Action Plan','2025-04-01','2027-04-01'),('O&M manual','O&M Manual','2024-01-15',None),('Instrument register','Instrument register 2026','2026-01-10',None),('Safety review','Comprehensive safety evaluation','2010-06-01',None)]:
  dse.post(B+f'/dams/{pz7}/documents',json={'docType':t,'title':ti,'status':'Approved','issueDate':iss,'nextReviewDate':rev})
after=dse.get(B+f'/dams/{pz7}/dhi').json()
c=after['current']
ok(len(c['components'])==6 and abs(sum(x['weight'] for x in c['components'])-1)<1e-9,'DHI has 6 components, weights sum to 1')
ok(doc(before)==0 and doc(c)==70,f"documentation score {doc(before)} → {doc(c)} (EAP + O&M + register; 2010 safety review older than 10 years not counted)")
ok(abs(c['dhi']-round(sum(x['weight']*x['score'] for x in c['components']),1))<0.11,f"DHI {c['dhi']} = Σ weight × score ({c['band']}), risk {c['risk']} ({c['rating']})")
ok(all(len(x['reasons'])>0 for x in c['components']),'every component explains its score')
snap=dse.post(B+f'/dams/{pz7}/dhi/snapshot').json(); ok(len(dse.get(B+f'/dams/{pz7}/dhi').json()['history'])>=1,'DHI snapshot stored in history')
ok(ins.post(B+f'/dams/{pz7}/dhi/snapshot').status_code==403,'inspector cannot record snapshots')
# ---------- availability ----------
av=dse.get(B+f'/reports/availability?damId={pz7}&days=30').json()
row={r['code']:r for r in av['rows']}
ok(row['DM001-PZ-09']['availabilityPct']>=95,f"availability report: PZ-09 {row['DM001-PZ-09']['availabilityPct']}% over 30 days")
ok(av['availabilityPct']>0 and 'longestGapHours' in row['DM001-PZ-09'],f"dam availability {av['availabilityPct']}%, valid {av['validPct']}%")
pf=dse.get(B+'/dashboard/portfolio').json(); ok(all(d['dhi'] is not None for d in pf['dams']) and pf['totals']['averageDhi'] is not None,'dashboard shows DHI and risk for every dam')
print('ALL PHASE 3 TESTS PASSED')
