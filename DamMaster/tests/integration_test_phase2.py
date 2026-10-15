"""Phase 2 end-to-end checks. Run AFTER integration_test.py on the same database.
Optional: set DM_MYSQL_CMD (e.g. "mysql -uroot dammaster") to also test time-based escalation."""
import os, sys, subprocess, requests
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; H={'X-Requested-With':'DamMaster'}
def S(): s=requests.Session(); s.headers.update(H); return s
def login(u,p):
  s=S(); r=s.post(B+'/auth/login',json={'username':u,'password':p}); assert r.status_code==200,(u,r.text); return s
def ok(c,m):
  print(('PASS ' if c else 'FAIL ')+m)
  if not c: sys.exit(1)
adm=login('admin','Admin@2026x'); dse=login('arjun','Temp@2026'); vik=login('vikram','Temp@2026'); app_=login('verma','Temp@2026')
users={u['username']:u for u in adm.get(B+'/users').json()}
ok(adm.put(B+f"/users/{users['arjun']['id']}",json={'fullName':'Dr. Arjun Mehta','role':'DamSafetyEngineer','isActive':True,'email':'arjun@example.org','phone':'+91 98100 00001'}).status_code==204,'user contact details saved')
adm.put(B+f"/users/{users['verma']['id']}",json={'fullName':'S. K. Verma','role':'Approver','isActive':True,'email':'verma@example.org'})
ok(adm.post(B+'/users',json={'username':'meera','fullName':'Meera Nair','role':'Inspector','password':'Temp@2026'}).status_code==200,'inspector created')
ins=login('meera','Temp@2026')
dam=dse.get(B+'/dams').json()[0]; D=dam['id']
# ---- EAP contacts
ok(ins.post(B+f'/dams/{D}/eap-contacts',json={'name':'x','email':'a@b.co'}).status_code==403,'inspector cannot edit EAP contacts')
ok(dse.post(B+f'/dams/{D}/eap-contacts',json={'name':'Bad','email':'not-an-email'}).status_code==400,'invalid e-mail rejected')
ok(dse.post(B+f'/dams/{D}/eap-contacts',json={'name':'District Collector','organisation':'District Administration','eapRole':'Evacuation authority','phone':'+91 98100 11111','email':'collector@example.org','minLevel':2,'escalationOrder':1}).status_code==200,'EAP contact order 1 added')
ok(dse.post(B+f'/dams/{D}/eap-contacts',json={'name':'Chief Engineer, SDSO','organisation':'State Dam Safety Organisation','eapRole':'Regulator','email':'sdso@example.org','minLevel':2,'escalationOrder':2}).status_code==200,'EAP contact order 2 added')
# ---- notifications for the Level 2 alert from Phase 1 (PZ-07)
al=dse.get(B+f'/alerts?damId={D}').json(); pz=[a for a in al if a.get('sensorCode')=='DM001-PZ-07' and a['ruleType']=='Threshold'][0]
ok(pz['ackDueAt'] is not None,'Level 2 alert has an acknowledgement deadline')
nn=dse.get(B+f"/alerts/{pz['id']}/notifications").json(); ok(any(n['channel']=='InApp' for n in nn),'in-app notifications created for Level 2 roles')
ok(pz['inspectionId'] is not None,'special inspection auto-created for Level 2 alert')
# ---- new Level 3 alert after EAP contacts exist: turbidity 120 NTU twice (emergency 100)
g=vik.post(B+'/gateways',json={'code':'GW-DM001-02','damId':D}).json()
msg={'messageId':'m-2','gatewayId':'GW-DM001-02','damId':'DM-001','readings':[{'sensorId':'DM001-TB-01','parameterCode':'TRB','observedAt':t,'value':120.0,'unit':'NTU','loggerStatus':'OK'} for t in ['2026-10-05T12:00:00+05:30','2026-10-05T18:00:00+05:30']]}
r=requests.post(B+'/telemetry',json=msg,headers={'X-Gateway-Code':'GW-DM001-02','X-Api-Key':g['apiKey']}); ok(r.status_code==202,'telemetry with turbid seepage accepted')
al=dse.get(B+f'/alerts?damId={D}').json(); tb=[a for a in al if a.get('sensorCode')=='DM001-TB-01' and a['ruleType']=='Threshold']
ok(tb and tb[0]['level']==3,'turbidity 120 NTU (2 readings) → Level 3 EMERGENCY')
nn=dse.get(B+f"/alerts/{tb[0]['id']}/notifications").json()
ch={(n['channel'],n['recipient']) for n in nn}
ok(('SMS','+91 98100 11111') in ch and ('Email','collector@example.org') in ch,'EAP contact (order 1) gets SMS + e-mail')
ok(('Email','sdso@example.org') not in ch,'order-2 contact not yet notified')
ok(('SMS','+91 98100 00001') in ch and ('Email','arjun@example.org') in ch,'dam safety engineer gets SMS + e-mail')
ok(sum(1 for n in nn if n['channel']=='InApp')>=4,'in-app notifications for all Level 3 roles')
ok(ins.get(B+'/notifications/log').status_code==403,'inspector cannot view notification log')
run=adm.post(B+'/notifications/run').json(); ok(run['dispatched']>=4,'dispatcher processed queued messages')
log=adm.get(B+'/notifications/log').json(); ok(all(n['status'] in ('Simulated','Delivered','Sent') for n in log if n['alertId']==tb[0]['id']),'without gateways configured, messages are recorded as Simulated')
# ---- escalation (needs DB access to move the deadline into the past)
cmd=os.environ.get('DM_MYSQL_CMD')
if cmd:
  subprocess.run(cmd.split()+['-e',f"UPDATE alert SET ack_due_at=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 MINUTE) WHERE id='{tb[0]['id']}'"],check=True)
  adm.post(B+'/notifications/run'); a=dse.get(B+f"/alerts/{tb[0]['id']}").json()['alert']
  ok(a['escalationStep']==1,'unacknowledged Level 3 alert escalated to step 1')
  nn=dse.get(B+f"/alerts/{tb[0]['id']}/notifications").json(); ok(any(n['recipient']=='sdso@example.org' and n['escalationStep']==1 for n in nn),'escalation notifies the order-2 EAP contact')
  ok(dse.post(B+f"/alerts/{tb[0]['id']}/acknowledge",json={'note':'On site, outfall being inspected'}).status_code==204,'alert acknowledged')
  subprocess.run(cmd.split()+['-e',f"UPDATE alert SET ack_due_at=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 MINUTE) WHERE id='{tb[0]['id']}'"],check=True)
  adm.post(B+'/notifications/run'); ok(dse.get(B+f"/alerts/{tb[0]['id']}").json()['alert']['escalationStep']==1,'acknowledged alert stops escalating')
else: print('SKIP escalation timing checks (DM_MYSQL_CMD not set)')
# ---- inspection workflow
ok(vik.post(B+'/inspections',json={'damId':D,'inspectionType':'Pre-monsoon','scheduledDate':'2026-05-20'}).status_code==403,'instrumentation engineer cannot plan inspections')
ok(ins.post(B+'/inspections',json={'damId':D,'inspectionType':'Yearly','scheduledDate':'2026-05-20'}).status_code==400,'unknown inspection type rejected')
i=ins.post(B+'/inspections',json={'damId':D,'inspectionType':'Pre-monsoon','scheduledDate':'2026-05-20','inspector':'Meera Nair'}).json(); I=i['id']
ok(ins.put(B+f'/inspections/{I}/checks',json=[{'itemCode':'ALL-01','result':'OK'}]).status_code==400,'checks need the inspection started')
ok(ins.post(B+f'/inspections/{I}/start').status_code==204,'inspection started')
det=ins.get(B+f'/inspections/{I}').json(); items=det['checklist']; ok(len(items)==12 and all(x['materialGroup'] in ('All','Concrete') for x in items),'checklist = 8 general + 4 concrete-dam items')
ok(ins.put(B+f'/inspections/{I}/checks',json=[{'itemCode':'CON-02','result':'Attention'}]).status_code==400,'Attention needs a remark')
ok(ins.put(B+f'/inspections/{I}/checks',json=[{'itemCode':x['code'],'result':'OK'} for x in items[:6]]).status_code==204,'6 checks recorded')
ok('not yet recorded' in ins.post(B+f'/inspections/{I}/complete',json={'overallCondition':'Fair'}).json().get('error',''),'cannot complete with checklist items missing')
sens={s['code']:s['id'] for s in dse.get(B+f'/dams/{D}/sensors').json()}
f=ins.post(B+f'/inspections/{I}/findings',json={'component':'Downstream face & toe','location':'Block 12 toe','description':'Sand boil with turbid flow near block 12 toe','severity':'Critical','sensorId':sens['DM001-TB-01'],'recommendation':'Reverse filter, continuous watch, lower reservoir per EAP'}).json()
ok(f.get('alertId') is not None,'Critical finding raises an alert')
a=dse.get(B+f"/alerts/{f['alertId']}").json()['alert']; ok(a['level']==3 and a['ruleType']=='Inspection','finding alert is Level 3, rule Inspection')
f2=ins.post(B+f'/inspections/{I}/findings',json={'component':'Spillway & gates','description':'Hoist rope wear on gate 3','severity':'Medium'}).json(); ok(f2.get('alertId') is None and f2['dueDate'],'Medium finding: no alert, due date set')
ok(ins.put(B+f'/inspections/{I}/checks',json=[{'itemCode':x['code'],'result':'Attention' if x['code']=='ALL-06' else 'OK','remark':'Sand boil at block 12 toe' if x['code']=='ALL-06' else None} for x in items]).status_code==204,'all checks recorded')
ok(ins.post(B+f'/inspections/{I}/complete',json={'overallCondition':'Poor','summary':'Sand boil found; EAP actions started'}).status_code==204,'inspection completed')
comp=dse.get(B+'/inspections/compliance?year=2026').json()[0]
ok(comp['preStatus']=='Done late' and comp['postStatus'] in ('Due','Overdue'),f"monsoon compliance: pre {comp['preStatus']}, post {comp['postStatus']}")
ok(ins.post(B+f"/inspections/findings/{f['id']}/status",json={'status':'Closed','note':'x'}).status_code==403,'inspector cannot close findings')
ok(dse.post(B+f"/inspections/findings/{f['id']}/status",json={'status':'Closed'}).status_code==400,'closing needs a note')
ok(ins.post(B+f"/inspections/findings/{f2['id']}/status",json={'status':'Assigned'}).status_code==204,'inspector assigns a finding')
ok(dse.post(B+f"/inspections/findings/{f['id']}/status",json={'status':'Closed','note':'Reverse filter placed; boil stopped; daily watch'}).status_code==204,'DSE closes finding with note')
# ---- reading schedule
sc=dse.get(B+f'/dams/{D}/schedule').json(); rows={r['code']:r for r in sc['rows']}
ok(sc['lifePhase']=='Long-term operation','schedule uses the dam life phase')
ok(rows['DM001-RL-01']['requiredHours']==1 and rows['DM001-RL-01']['loggerSlowerThanRequired'],'reservoir level: hourly required, logger at 6 h flagged')
ok(rows['DM001-PZ-07']['requiredHours']==6 and rows['DM001-PZ-07']['compliancePct'] is not None,'piezometer: 6-hourly required, compliance computed')
ok(rows['DM001-RG-01']['status']!='','rainfall row present')
# ---- inbox & dashboard
c=dse.get(B+'/notifications/mine/count').json()['unread']; ok(c>0,f'DSE has {c} unread in-app notifications')
dse.post(B+'/notifications/mine/read',json={}); ok(dse.get(B+'/notifications/mine/count').json()['unread']==0,'mark all read')
t=dse.get(B+'/dashboard/portfolio').json()['totals']; ok('openFindings' in t and t['openFindings']>=1,'dashboard shows open findings')
print('ALL PHASE 2 TESTS PASSED')
