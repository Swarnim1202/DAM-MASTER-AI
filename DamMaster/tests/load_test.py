import requests, time, datetime as dt, random, json
import os
B=os.environ.get('DM_API','http://localhost:5080')+'/api'; s=requests.Session(); s.headers.update({'X-Requested-With':'DamMaster'})
s.post(B+'/auth/login',json={'username':'vikram','password':os.environ.get('DM_PASSWORD','Temp@2026')})
# 10 sensors x 10 000 readings = 100 000 readings (15-minute data, ~104 days)
sensors=[{'sensorId':f'LT-PZ-{i:02d}','damId':'DM-LOAD','parameterCode':'UPL','engUnit':'kPa','expectedMin':0,'expectedMax':500,'direction':'Rising','readingIntervalMin':15} for i in range(10)]
end=dt.datetime(2026,10,1,tzinfo=dt.timezone.utc)
rd=[{'damId':'DM-LOAD','sensorId':x['sensorId'],'parameterCode':'UPL','observedAt':(end-dt.timedelta(minutes=15*k)).strftime('%Y-%m-%dT%H:%M:00Z'),'value':round(150+random.gauss(0,2),2),'unit':'kPa'} for x in sensors for k in range(10000)]
p={'fileName':'load','commit':True,'dams':[{'damId':'DM-LOAD','damName':'Load Test Dam','damType':'Concrete gravity','hazardClass':'Low'}],'sensors':sensors,'readings':rd}
body=json.dumps(p); print('payload MB',round(len(body)/1e6,1))
t=time.time(); r=s.post(B+'/import',data=body,headers={'Content-Type':'application/json'}); el=time.time()-t
j=r.json(); print('status',r.status_code,'good',j.get('good'),'suspect',j.get('suspect'),'seconds',round(el,1),'readings/s',round(len(rd)/el))
t=time.time(); r=s.post(B+'/import',data=body,headers={'Content-Type':'application/json'}); el=time.time()-t; print('re-import (all duplicates) seconds',round(el,1),'dups',r.json().get('duplicates'))
sid=[x['id'] for x in s.get(B+'/dams').json() if x['code']=='DM-LOAD'][0]
sen=s.get(B+f'/dams/{sid}/sensors').json()[0]['id']
t=time.time(); pts=s.get(B+f'/sensors/{sen}/readings?days=0').json(); print('chart query all-data points',len(pts),'type',pts[0]['q'],'ms',round((time.time()-t)*1000))
