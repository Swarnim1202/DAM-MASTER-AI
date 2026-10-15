import requests, openpyxl, datetime as dt, json, sys
import os
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.status_code,r.text); return s
ok=lambda c,m: print(('PASS ' if c else 'FAIL ')+m) or (c or sys.exit(1))
# CSRF guard
r=requests.post(B+'/auth/login',json={'username':'admin','password':'x'}); ok(r.status_code==400,'CSRF header required')
r=S().post(B+'/auth/login',json={'username':'admin','password':'wrong'}); ok(r.status_code==401,'wrong password rejected')
adm=login('admin','ChangeMe@2026'); me=adm.get(B+'/auth/me').json(); ok(me['mustChangePassword'] is True,'bootstrap admin must change password')
r=adm.post(B+'/auth/change-password',json={'currentPassword':'ChangeMe@2026','newPassword':'Admin@2026x'}); ok(r.status_code==204,'admin changed password')
adm=login('admin','Admin@2026x')
users={}
for u,n,role in [('arjun','Dr. Arjun Mehta','DamSafetyEngineer'),('vikram','Vikram Singh','InstrumentationEngineer'),('verma','S. K. Verma','Approver'),('director','Director Projects','Management')]:
  r=adm.post(B+'/users',json={'username':u,'fullName':n,'role':role,'password':'Temp@2026'}); ok(r.status_code==200,'create user '+u)
ok(adm.post(B+'/users',json={'username':'x y','fullName':'a','role':'Admin','password':'Temp@2026'}).status_code==400,'invalid username rejected')
vik=login('vikram','Temp@2026'); dse=login('arjun','Temp@2026'); app_=login('verma','Temp@2026'); mg=login('director','Temp@2026')
# reference
for c in ['dam-types','failure-modes','parameters','instrument-types','standards','interpretations']:
  ok(len(vik.get(B+'/reference/'+c).json())>0,'catalogue '+c)
# Build import payload from the sample workbook (same as the React page does)
wb=openpyxl.load_workbook(os.path.join(os.path.dirname(__file__),'..','samples','DAM_MASTER_IoT_Sample_RiverValley_90days.xlsx'),data_only=True)
def rows(name):
  ws=wb[name]; it=ws.iter_rows(values_only=True); hd=[h for h in next(it)]
  return [dict(zip(hd,r)) for r in it if any(v is not None for v in r)]
def camel(k): p=k.split('_'); return p[0]+''.join(x.title() for x in p[1:])
def conv(d):
  o={}
  for k,v in d.items():
    if k is None: continue
    if isinstance(v,dt.datetime): v=v.strftime('%Y-%m-%dT%H:%M:00+05:30')
    elif isinstance(v,dt.date): v=v.strftime('%Y-%m-%d')
    o[camel(k)]=v
  return o
dams=[conv(r) for r in rows('Dam_Master')]; sens=[conv(r) for r in rows('Sensor_Master')]; rds=[conv(r) for r in rows('Readings')]
for r in rds:
  if isinstance(r.get('observedAt'),str) and 'T' not in r['observedAt']: pass
payload={'fileName':'DAM_MASTER_IoT_Sample_RiverValley_90days.xlsx','commit':False,'dams':dams,'sensors':sens,'readings':rds}
ok(mg.post(B+'/import',json=payload).status_code==403,'management cannot import')
pre=vik.post(B+'/import',json=payload).json()
print(' preview:',{k:pre[k] for k in ['rowsRead','good','suspect','bad','rejected','quarantined','duplicates','damsUpserted','sensorsUpserted','committed']})
ok(pre['rejected']==3 and pre['quarantined']==1 and pre['duplicates']==1 and pre['bad']==2,'validation catches the 7 seeded errors')
ok(len(vik.get(B+'/dams').json())==0,'preview wrote nothing')
payload['commit']=True; res=vik.post(B+'/import',json=payload).json()
ok(res['committed'] and res['good']==pre['good'],'commit import')
print(' alerts raised:',[(a['level'],a['ruleType'],a['title']) for a in res['alertsRaised']])
res2=vik.post(B+'/import',json=payload).json(); ok(res2['duplicates']>=2880,'re-import detected as duplicates')
dam=vik.get(B+'/dams').json()[0]; sl=vik.get(B+f"/dams/{dam['id']}/sensors").json(); ok(len(sl)==8,'8 sensors registered')
byc={s['code']:s for s in sl}
ok(byc['DM001-PZ-07']['alertValue']==215 and not byc['DM001-PZ-07']['thresholdApproved'],'thresholds imported, pending approval')
# threshold approval with segregation of duties
th=vik.get(B+f"/sensors/{byc['DM001-SW-02']['id']}/thresholds").json()[0]
ok(app_.post(B+f"/sensors/thresholds/{th['id']}/approve").status_code==204,'approver approves imported threshold')
# new threshold version with rate limit, by DSE, cannot self-approve
r=dse.post(B+f"/sensors/{byc['DM001-SW-02']['id']}/thresholds",json={'alertValue':100,'alarmValue':150,'emergencyValue':200,'rateLimitPerDay':0.5,'zLimit':2.5,'persistence':2,'method':'Statistical','basis':'Historical envelope + 2σ (test)'}).json()
ok(dse.post(B+f"/sensors/thresholds/{r['id']}/approve").json().get('error','').startswith('Segregation'),'author cannot approve own threshold')
ok(dse.post(B+f"/sensors/{byc['DM001-SW-02']['id']}/thresholds",json={'alertValue':150,'alarmValue':100,'method':'Design','basis':'x'}).status_code==400,'out-of-order levels rejected')
# PFMA linking seepage + turbidity + piezometer
pf=dse.post(B+'/pfma',json={'damId':dam['id'],'failureModeCode':'PIPING','location':'Left bank gallery outfall','description':'Internal erosion through foundation joints','sensorIds':[byc['DM001-SW-02']['id'],byc['DM001-TB-01']['id'],byc['DM001-PZ-07']['id']]}).json()
ok('id' in pf,'PFMA created with 3 linked sensors')
ev=dse.post(B+f"/dams/{dam['id']}/evaluate").json()
print(' evaluate:',[(a['level'],a['ruleType'],a['title']) for a in ev])
al=dse.get(B+f"/alerts?damId={dam['id']}").json()
print(' active alerts:',[(a['level'],a['ruleType'],a.get('sensorCode'),a['status']) for a in al])
sw=[a for a in al if a.get('sensorCode')=='DM001-SW-02' and a['ruleType']=='Threshold']
ok(len(sw)==1 and sw[0]['level']>=1,'SW-02 threshold alert raised with new threshold 100 L/min')
ok(not [a for a in al if a['ruleType']=='Corroboration'],'no corroboration with only one abnormal instrument')
det=dse.get(B+f"/alerts/{sw[0]['id']}").json(); ok(len(det['interpretations'])>=3 and det['series'],'alert detail has interpretation hints and readings')
ok(mg.post(B+f"/alerts/{sw[0]['id']}/acknowledge",json={'note':'x'}).status_code==403,'management cannot acknowledge')
ok(vik.post(B+f"/alerts/{sw[0]['id']}/acknowledge",json={'note':'Weir checked, reading valid'}).status_code==204,'instrumentation engineer acknowledges')
ok(vik.post(B+f"/alerts/{sw[0]['id']}/close",json={'disposition':'Confirmed','note':'x'}).status_code==403,'only DSE closes')
ok(dse.post(B+f"/alerts/{sw[0]['id']}/close",json={'disposition':'Maybe','note':'x'}).status_code==400,'invalid disposition rejected')
# telemetry
g=vik.post(B+'/gateways',json={'code':'GW-DM001-01','damId':dam['id']}).json(); key=g['apiKey']
msg={'messageId':'m-1','gatewayId':'GW-DM001-01','damId':'DM-001','readings':[{'sensorId':'DM001-PZ-07','parameterCode':'UPL','observedAt':'2026-10-05T00:00:00+05:30','value':236.0,'unit':'kPa','loggerStatus':'OK'},{'sensorId':'DM001-PZ-07','parameterCode':'UPL','observedAt':'2026-10-05T06:00:00+05:30','value':237.5,'unit':'kPa','loggerStatus':'OK'}]}
ok(requests.post(B+'/telemetry',json=msg,headers={'X-Gateway-Code':'GW-DM001-01','X-Api-Key':'bad'}).status_code==401,'telemetry rejects bad key')
r=requests.post(B+'/telemetry',json=msg,headers={'X-Gateway-Code':'GW-DM001-01','X-Api-Key':key}); ok(r.status_code==202 and r.json()['accepted']==2,'telemetry accepted 2 readings')
al=dse.get(B+f"/alerts?damId={dam['id']}").json(); pz=[a for a in al if a.get('sensorCode')=='DM001-PZ-07' and a['ruleType']=='Threshold']
ok(pz and pz[0]['level']==2,'PZ-07 uplift 237.5 > alarm 235 for 2 readings → Level 2 ALARM')
cor=[a for a in al if a['ruleType']=='Corroboration']
ok(len(cor)==1 and cor[0]['level']>=2,'corroboration: SW-02 + PZ-07 abnormal on PIPING PFMA → Level 2')
print(' active alerts now:',[(a['level'],a['ruleType'],a.get('sensorCode') or 'PFMA',a['status']) for a in al])
ok(dse.post(B+f"/alerts/{sw[0]['id']}/close",json={'disposition':'Confirmed','note':'Seepage rise confirmed; daily readings ordered'}).status_code==204,'DSE closes with disposition')
# standards verification
ok(vik.post(B+'/reference/standards/IS6512/verify',json={'edition':'x'}).status_code==403,'instr cannot verify standards')
ok(app_.post(B+'/reference/standards/IS6512/verify',json={'edition':'','clauses':''}).status_code==400,'edition required')
ok(app_.post(B+'/reference/standards/IS6512/verify',json={'edition':'1984 (to confirm)','clauses':'Load combinations'}).status_code==204,'approver verifies a standard')
d=mg.get(B+'/dashboard/portfolio').json(); print(' totals:',d['totals']); ok(d['totals']['dams']==1 and d['totals']['readings']>=2880,'portfolio dashboard')
ok(len(adm.get(B+'/audit').json())>10,'audit trail recorded')
ok(adm.put(B+f"/users/{me['id']}",json={'fullName':'x','role':'Approver','isActive':True}).status_code==400,'admin cannot change own role')
print('ALL INTEGRATION TESTS PASSED')
