"""Phase 6 end-to-end checks: map, InSAR / survey datasets, document files + full-text search + AI answers, live section, design → thresholds.
Run after the Phase 1–5 tests. The API must be started with Ai__Endpoint=http://127.0.0.1:5099/v1/chat/completions (mock started here)."""
import os, sys, json, math, random, threading, requests
from http.server import BaseHTTPRequestHandler, HTTPServer
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}; SAMPLES=os.path.join(os.path.dirname(__file__),'..','samples')
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
dse=login('arjun','Temp@2026'); vik=login('vikram','Temp@2026'); ins=login('meera','Temp@2026'); app_=login('verma','Temp@2026'); chk=login('iyer','Temp@2026')
dam=[d for d in dse.get(B+'/dams').json() if d['code']=='DM-001'][0]; D=dam['id']
dam.update({'latitude':19.0760,'longitude':73.8567}); ok(dse.put(B+f'/dams/{D}',json=dam).status_code==200,'dam coordinates set')
sens={s['code']:s['id'] for s in dse.get(B+f'/dams/{D}/sensors').json()}
geo={'DM001-PZ-07':(742,12.0),'DM001-PZ-09':(741.5,30.0),'DM001-SW-02':(745,45.0),'DM001-RL-01':(818.5,0.0)}
for k,(el,off) in geo.items():
  s=dse.get(B+f"/sensors/{sens[k]}").json(); s.update({'latitude':19.0760+off*1e-5,'longitude':73.8567,'offsetM':off,'elevationM':el,'chainageM':300.0})
  ok(vik.put(B+f"/sensors/{sens[k]}",json=s).status_code==200,f'{k}: coordinates and position on the section')
m=dse.get(B+'/map').json(); ok(any(x['id']==D and x['lat']==19.076 for x in m['dams']) and len([x for x in m['sensors'] if x['damId']==D])==4,'map: dam and 4 located sensors')
# ---------- InSAR ----------
random.seed(1); pts=[]
for k in range(200):
  r=random.uniform(0,3000); a=random.uniform(0,2*math.pi); lat=19.0760+r*math.cos(a)/111000; lon=73.8567+r*math.sin(a)/105000
  v=random.gauss(-1,1.5)
  if k<5: lat,lon,v=19.0760+0.002*k/5,73.8567,-15.0-k   # 5 fast points near the dam
  if k in (5,6): lat,lon,v=19.1,73.88,-22.0             # 2 fast points on the rim (~3 km)
  pts.append({'pointId':f'PS{k:04d}','latitude':round(lat,6),'longitude':round(lon,6),'value':round(v,2),'coherence':0.8})
req={'damId':D,'kind':'InSAR','name':'Sentinel-1 2024–2026','provider':'InSAR service','periodFrom':'2024-10-01','periodTo':'2026-09-30','threshold':10,'points':pts}
ok(ins.post(B+'/surveys',json=req).status_code==403,'inspector cannot import remote-sensing data')
bad=dict(req,points=[{'pointId':'X','latitude':95,'longitude':73,'value':1}]); ok('Invalid points' in vik.post(B+'/surveys',json=bad).json()['error'],'coordinates out of range refused')
r=vik.post(B+'/surveys',json=req).json(); ok(r['pointCount']==200 and r['alert'] is not None,f"InSAR dataset imported (200 points) → alert: {r['alert']['title']}")
a=[x for x in dse.get(B+f'/alerts?damId={D}').json() if x['ruleType']=='InSAR'][0]
ok('5 of them within 1000 m' in a['detail'] and '2 on the rim' in a['detail'],'alert separates points at the structure (5) from the rim (2)')
# ---------- survey campaigns ----------
base=[{'pointId':f'CR-{k:02d}','latitude':19.076+k*1e-4,'longitude':73.8567,'value':818.500,'label':f'Crest {k*50} m'} for k in range(10)]
later=[dict(p,value=round(p['value']-(0.002+0.001*k),4)) for k,p in enumerate(base)]
s1=vik.post(B+'/surveys',json={'damId':D,'kind':'Survey','name':'Crest levelling 2025','epochDate':'2025-03-01','points':base}).json()
ok('epoch' in vik.post(B+'/surveys',json={'damId':D,'kind':'Survey','name':'no date','points':base}).json()['error'],'survey campaign needs an epoch date')
s2=vik.post(B+'/surveys',json={'damId':D,'kind':'Survey','name':'Crest levelling 2026','epochDate':'2026-03-01','points':later}).json()
cmp=dse.get(B+f"/surveys/compare?a={s2['id']}&b={s1['id']}").json()
ok(cmp['matched']==10 and cmp['maxSettlementMm']==-11.0 and abs(cmp['years']-1.0)<0.01,f"campaign comparison: 10 points matched, max settlement {cmp['maxSettlementMm']} mm in {cmp['years']} year (order-independent)")
# ---------- documents: files, extraction, search ----------
docs=dse.get(B+f'/dams/{D}/documents').json()
doc=dse.post(B+f'/dams/{D}/documents',json={'docType':'Design report','title':'Dam safety monitoring — expert reference','status':'Approved'}).json()
with open(os.path.join(SAMPLES,'Dam_Safety_Monitoring_Expert_Reference.pdf'),'rb') as f:
  ok(ins.post(B+f"/dams/{D}/documents/{doc['id']}/files",files={'file':('ref.pdf',f,'application/pdf')}).status_code==403,'inspector cannot upload files')
with open(os.path.join(SAMPLES,'Dam_Safety_Monitoring_Expert_Reference.pdf'),'rb') as f:
  up=dse.post(B+f"/dams/{D}/documents/{doc['id']}/files",files={'file':('Dam_Safety_Monitoring_Expert_Reference.pdf',f,'application/pdf')}).json()
ok(up['extractedOk'] and up['pages']==19 and up['textChars']>20000,f"PDF uploaded: {up['pages']} pages, {up['textChars']} characters indexed")
doc2=dse.post(B+f'/dams/{D}/documents',json={'docType':'Design report','title':'Calculation handbook','status':'Approved'}).json()
with open(os.path.join(SAMPLES,'DAM_MASTER_AI_Calculation_Handbook.docx'),'rb') as f:
  up2=dse.post(B+f"/dams/{D}/documents/{doc2['id']}/files",files={'file':('handbook.docx',f,'application/vnd.openxmlformats-officedocument.wordprocessingml.document')}).json()
ok(up2['extractedOk'] and up2['textChars']>20000,f"Word document uploaded: {up2['textChars']} characters indexed")
ok('Allowed file types' in dse.post(B+f"/dams/{D}/documents/{doc['id']}/files",files={'file':('x.exe',b'MZ...','application/octet-stream')}).json()['error'],'executable files refused')
dl=dse.get(B+f"/files/{up['id']}"); ok(dl.status_code==200 and dl.content[:4]==b'%PDF','file download returns the original PDF')
hits=dse.get(B+'/library/search',params={'q':'turbidity seepage internal erosion','damId':D}).json()
pdfh=[h for h in hits[:5] if h['title']=='Dam safety monitoring — expert reference' and h['page'] is not None]
ok(len(hits)>0 and len(pdfh)>0,f"search 'turbidity seepage internal erosion': {len(hits)} passages; the PDF appears in the top 5 (page {pdfh[0]['page'] if pdfh else '-'})")
h2=dse.get(B+'/library/search',params={'q':'hydraulic jump sequent depth Froude'}).json(); ok(any(h['title']=='Calculation handbook' for h in h2[:3]),'search finds the hydraulic-jump formula in the Word handbook')
ok(dse.get(B+'/library/search',params={'q':'ab'}).status_code==400,'very short queries refused')
r=dse.post(B+'/library/ask',json={'question':'When should a turbidity alert be raised?','damId':D}).json()
ok(r['answer'] is None and len(r['sources'])>0 and 'unreachable' in r['note'],'AI service down → passages still returned with a clear note')
class Mock(BaseHTTPRequestHandler):
  def do_POST(self):
    body=json.loads(self.rfile.read(int(self.headers['Content-Length']))); Mock.last=body
    out=json.dumps({'choices':[{'message':{'content':'Turbid seepage indicates soil transport and must be treated as possible piping [1].'}}]}).encode()
    self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(out))); self.end_headers(); self.wfile.write(out)
  def log_message(self,*a): pass
srv=HTTPServer(('127.0.0.1',5099),Mock); threading.Thread(target=srv.serve_forever,daemon=True).start()
r=dse.post(B+'/library/ask',json={'question':'When should a turbidity alert be raised?','damId':D}).json()
ok(r['answer'] and '[1]' in r['answer'] and r['configured'],'AI answer generated from the passages, with citation')
ok('ONLY the numbered passages' in Mock.last['messages'][0]['content'] and '[1]' in Mock.last['messages'][1]['content'],'prompt restricts the model to the retrieved passages')
srv.shutdown()
# ---------- live section ----------
tw=dse.get(B+f'/dams/{D}/twin').json()
ok(tw['source'].startswith('Design project DP-001') and tw['geometry']['type']=='Concrete gravity','live section uses the linked design project geometry')
ok(len([s for s in tw['sensors'] if s['offsetM'] is not None])==4 and tw['reservoirLevel'] is not None,f"4 sensors placed on the section; reservoir {tw['reservoirLevel']} m")
# ---------- design → thresholds ----------
P=[p for p in dse.get(B+'/design/projects').json() if p['code']=='DP-001'][0]['id']
sg=dse.get(B+f'/dams/{D}/design-thresholds',params={'projectId':P}).json()
ok(not sg['canApply'] and 'Approved' in sg['note'],'suggestions shown but not applicable until Main dam stability is approved')
ok('must be Approved' in dse.post(B+f'/dams/{D}/design-thresholds',json={'projectId':P,'sensorIds':[sens['DM001-PZ-07']]}).json()['error'],'apply refused while the design is not approved')
ok(chk.post(B+f'/design/projects/{P}/calcs/dam/check').status_code==204,'checker checks Main dam stability')
for c in ['IS6512','IS1893_1']: app_.post(B+f'/reference/standards/{c}/verify',json={'edition':'(to confirm)','clauses':'stability criteria'})
ok(app_.post(B+f'/design/projects/{P}/calcs/dam/approve').status_code==204,'approver approves it (codes verified)')
sg=dse.get(B+f'/dams/{D}/design-thresholds',params={'projectId':P}).json(); rows={r['code']:r for r in sg['rows']}
proj=dse.get(B+f'/design/projects/{P}').json(); damd=[c for c in proj['calcs'] if c['code']=='dam'][0]['data']
exp=9.81*proj['inputs']['up']*(proj['inputs']['frl']-damd['fl'])*(1-12.0/damd['b'])
ok(sg['canApply'] and abs(rows['DM001-PZ-07']['alarm']-round(exp,1))<0.06,f"PZ-07 (12 m from heel): design uplift {rows['DM001-PZ-07']['alarm']} kPa = 9.81·u·(FRL − foundation)·(1 − x/B) ✓")
ok(abs(rows['DM001-PZ-07']['alert']-0.8*rows['DM001-PZ-07']['alarm'])<=0.1 and rows['DM001-PZ-07']['emergency']>rows['DM001-PZ-07']['alarm'],'alert = 80 % of design, emergency = drains-inoperative uplift')
ok(rows['DM001-RL-01']['alert']==proj['inputs']['frl'] and rows['DM001-RL-01']['emergency']==proj['inputs']['mwl'],'reservoir level: alert at FRL, emergency at MWL')
ok(vik.post(B+f'/dams/{D}/design-thresholds',json={'projectId':P,'sensorIds':[sens['DM001-PZ-07']]}).status_code==403,'instrumentation engineer cannot apply design thresholds')
ap=dse.post(B+f'/dams/{D}/design-thresholds',json={'projectId':P,'sensorIds':[sens['DM001-PZ-07'],sens['DM001-RL-01']]}).json(); ok(ap['applied']==2,'thresholds applied to PZ-07 and the reservoir level sensor')
th=dse.get(B+f"/sensors/{sens['DM001-PZ-07']}/thresholds").json()[0]
ok(th['method']=='Design' and 'DP-001' in th['basis'] and th['approvedBy'] is None,'new threshold version cites the design and still needs approval')
print('ALL PHASE 6 TESTS PASSED')
