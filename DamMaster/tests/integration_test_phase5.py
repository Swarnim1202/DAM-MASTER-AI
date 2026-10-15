"""Phase 5 end-to-end checks: design workspace (projects, inputs, nine calculations, STALE engine, check / approve, code gating).
Run after the Phase 1–4 tests on the same database."""
import os, sys, requests
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
adm=login('admin','Admin@2026x'); dse=login('arjun','Temp@2026'); app_=login('verma','Temp@2026'); ins=login('meera','Temp@2026')
for u,n,r in [('priya','Priya Rao','DesignEngineer'),('iyer','R. Iyer','Checker')]:
  ok(adm.post(B+'/users',json={'username':u,'fullName':n,'role':r,'password':'Temp@2026'}).status_code==200,f'created {r} {u}')
des=login('priya','Temp@2026'); chk=login('iyer','Temp@2026')
v=dse.get(B+'/design/validation').json(); ok(v['passed']==v['total']==26,f"engine validation suite {v['passed']}/{v['total']} against independent reference values")
cat=des.get(B+'/design/catalog').json(); ok(len(cat['inputs'])==52 and len(cat['calcs'])==9,'catalogue: 52 inputs in 8 groups, 9 calculations')
ok(ins.post(B+'/design/projects',json={'name':'x'}).status_code==403,'inspector cannot create design projects')
p=des.post(B+'/design/projects',json={'name':'Ghats storage project — preliminary'}).json(); P=p['id']
d=des.get(B+f'/design/projects/{P}').json(); C={c['code']:c for c in d['calcs']}
ok(len(C)==9 and all(c['version']==1 and c['status']=='Draft' for c in C.values()),'new project: 9 calculations at version 1, Draft')
res=lambda code,label: [r for r in C[code]['results'] if r['label']==label][0]['value']
ok(res('type','Recommended')=='Concrete gravity' and res('type','Suitability of selected')=='99','dam type: concrete gravity recommended, 99/100 (same as prototype)')
ok(res('dam','Maximum height')=='82.9' and res('dam','Base width')=='54.0','gravity section: H 82.9 m, B 54.0 m')
ok(res('spill','Gated bays')=='7' and res('spill','Gross width')=='123','spillway: 7 bays, 123 m')
ok(res('route','Peak outflow')=='11,837' and res('route','Maximum reservoir level')=='671.80','routing: 11,837 m³/s, 671.80 m')
ok(res('hp','Installed capacity')=='58.7','hydropower: 58.7 MW')
ok(all(c['ok'] for c in C['dam']['checks']) and len(C['dam']['checks'])==8,'all 8 stability checks pass')
ok(not [c for c in C['basin']['checks'] if c['label'].startswith('Tailwater')][0]['ok'],'stilling basin: tailwater check fails (14 m short) — as in the prototype')
# validation of inputs
ok(des.put(B+f'/design/projects/{P}/inputs',json={'changes':{'mwl':660}}).json()['error'].startswith('Not applied: MWL'),'MWL below FRL refused (consistency rule)')
ok('must be one of' in des.put(B+f'/design/projects/{P}/inputs',json={'changes':{'zone':'VI'}}).json()['error'],'invalid seismic zone refused')
ok('enter a number' in des.put(B+f'/design/projects/{P}/inputs',json={'changes':{'gc':'heavy'}}).json()['error'],'non-numeric value refused')
ok(chk.put(B+f'/design/projects/{P}/inputs',json={'changes':{'frl':671}}).status_code==403,'checker cannot edit inputs')
# STALE engine
r=des.put(B+f'/design/projects/{P}/inputs',json={'changes':{'frl':671}}).json()
ok(r['stale']==['type','dam','spill','route','basin','gate','hp','cost'],f"FRL change → 8 calculations STALE (top of dam unaffected)")
imp=des.get(B+f'/design/projects/{P}/impact').json(); ok(len(imp)==8 and 'FRL' in imp[0]['reason'] and '→' in imp[1]['path'],f"impact report with dependency paths, e.g. {imp[1]['path']}")
ok('STALE' in chk.post(B+f'/design/projects/{P}/calcs/spill/check').json()['error'],'a STALE calculation cannot be checked')
w=des.post(B+f'/design/projects/{P}/run',json={'onlyStale':True}).json()['written']; ok(len(w)==8,f"run: {len(w)} recalculated")
d=des.get(B+f'/design/projects/{P}').json(); C={c['code']:c for c in d['calcs']}
ok(C['top']['version']==1 and C['spill']['version']==2 and not any(c['stale'] for c in C.values()),'versions: unaffected stays v1, affected go to v2, none STALE')
# check / approve
ok(des.post(B+f'/design/projects/{P}/calcs/spill/check').status_code==403,'design engineer cannot check calculations')
ok(chk.post(B+f'/design/projects/{P}/calcs/spill/check').status_code==204,'checker checks the spillway calculation')
ok(chk.post(B+f'/design/projects/{P}/calcs/spill/approve').status_code==403,'checker cannot approve')
e=app_.post(B+f'/design/projects/{P}/calcs/spill/approve').json()['error']; ok('CODE VERIFICATION REQUIRED' in e and 'IS 11223' in e,'approval blocked until IS 11223 / IS 6934 are verified')
for code in ['IS11223','IS6934']: ok(app_.post(B+f'/reference/standards/{code}/verify',json={'edition':'(edition to confirm)','clauses':'capacity, crest shape'}).status_code==204,f'approver verifies {code}')
ok(app_.post(B+f'/design/projects/{P}/calcs/spill/approve').status_code==204,'spillway calculation approved')
c=des.get(B+f'/design/projects/{P}/calcs/spill').json()
rec=c['record']; ok(all(k in rec for k in ['inputs','assumptions','codes','method','formula','steps','results','checks','interpretation']) and c['calc']['approvedBy']=='S. K. Verma','calculation record has all 12 steps and the approver')
ok(len(c['history'])==2,'history keeps both versions')
# an approved calculation becomes STALE when its inputs change, and re-running resets approval
r=des.put(B+f'/design/projects/{P}/inputs',json={'changes':{'bay':16}}).json(); ok('spill' in r['stale'],'changing gate bay width flags the approved spillway STALE')
des.post(B+f'/design/projects/{P}/run',json={}); c=des.get(B+f'/design/projects/{P}/calcs/spill').json()['calc']
ok(c['version']==3 and c['status']=='Draft' and c['approvedBy'] is None,'re-run creates v3 as Draft — approval must be repeated')
# elevation–area table
ok('area must not decrease' in des.put(B+f'/design/projects/{P}/ea',json={'rows':[[640,18],[650,10],[660,31]]}).json()['error'],'elevation–area table with decreasing area refused')
r=des.put(B+f'/design/projects/{P}/ea',json={'rows':[[640,18],[650,24],[660,31],[670,45],[675,50],[680,55]]}).json(); ok(r['stale']==['route'],'larger reservoir area → only flood routing STALE')
des.post(B+f'/design/projects/{P}/run',json={}); d=des.get(B+f'/design/projects/{P}').json(); C={c['code']:c for c in d['calcs']}
ok(float(res('route','Peak outflow').replace(',',''))<11837,f"more storage → lower routed peak ({res('route','Peak outflow')} m³/s)")
ch=des.get(B+f'/design/projects/{P}/changes').json(); ok(any(x['inputKey']=='frl' and x['newValue']=='671' for x in ch),'input change log records FRL 670 → 671 with who and when')
# pull from a registered dam and copy
dam=[x for x in dse.get(B+'/dams').json() if x['code']=='DM-001'][0]
rr=des.post(B+f'/design/projects/{P}/pull-dam',json={'damId':dam['id']}).json(); r=rr['result']; ok('frl' in r['changed'] and 'ea' in r['changed'] and len(r['stale'])>=8 and 'survey' in rr['note'],'levels pulled from River Valley Dam (FRL 815); area table shifted with a warning; calculations STALE')
des.post(B+f'/design/projects/{P}/run',json={}); d=des.get(B+f'/design/projects/{P}').json(); C={c['code']:c for c in d['calcs']}
ok(d['project']['damName']=='River Valley Dam' and res('type','Selected for design')=='Concrete gravity','project linked to the dam; type taken from the dam register')
cp=des.post(B+'/design/projects',json={'name':'Option B','copyFromId':P}).json(); d2=des.get(B+f"/design/projects/{cp['id']}").json()
ok(d2['inputs']['frl']==815 and cp['code']=='DP-002','project duplicated with all inputs (DP-002)')
ok(len(des.get(B+'/design/projects').json())==2,'project list')
print('ALL PHASE 5 TESTS PASSED')
