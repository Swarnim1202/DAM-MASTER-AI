import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, Finding, Inspection, InspectionItem, SensorStatus } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { InspectionBadge, SeverityBadge } from '../components/StatusBadges';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';
import { fmtDate } from './Inspections';
import FindingPhotos from '../components/FindingPhotos';

interface Detail { inspection: Inspection; dam: Dam; checklist: InspectionItem[]; findings: Finding[] }
const RESULTS = ['OK', 'Attention', 'Not applicable'];
const SEVERITIES = ['Low', 'Medium', 'High', 'Critical'];
const CONDITIONS = ['Good', 'Fair', 'Poor', 'Unsatisfactory'];

export default function InspectionDetail() {
  const { id } = useParams(); const { can } = useAuth(); const toast = useToast();
  const { data, error, reload } = useApi<Detail>('/inspections/' + id);
  const sensors = useApi<SensorStatus[]>(data ? `/dams/${data.inspection.damId}/sensors` : null);
  const [checks, setChecks] = useState<Record<string, { result: string; remark: string }>>({});
  const [err, setErr] = useState<string | null>(null);
  const [addF, setAddF] = useState(false); const [done, setDone] = useState(false); const [closeF, setCloseF] = useState<Finding | null>(null);
  const [nf, setNf] = useState({ component: '', location: '', description: '', severity: 'Medium', sensorId: '', recommendation: '' });
  const [cond, setCond] = useState({ overallCondition: 'Good', summary: '' }); const [note, setNote] = useState('');
  useEffect(() => { if (data) setChecks(Object.fromEntries(data.checklist.map((c) => [c.code, { result: c.result ?? '', remark: c.remark ?? '' }]))); }, [data]);
  if (!data) return <ErrorBox error={error} />;
  const i = data.inspection; const live = i.status === 'In progress' && can('manageInspections');
  const run = async (fn: () => Promise<unknown>, msg: string) => { try { await fn(); setErr(null); toast(msg); void reload(); return true; } catch (e) { setErr((e as Error).message); return false; } };
  const saveChecks = () => run(() => api.put(`/inspections/${id}/checks`, Object.entries(checks).filter(([, v]) => v.result).map(([itemCode, v]) => ({ itemCode, result: v.result, remark: v.remark || null }))), 'Checklist saved.');
  const components = [...new Set(data.checklist.map((c) => c.component))];
  const answered = Object.values(checks).filter((c) => c.result).length;
  return (
    <>
      <div className="hero"><div><h1>{i.inspectionType} inspection — {i.damName}</h1><p>Scheduled {fmtDate(i.scheduledDate)} · <InspectionBadge s={i.status} /> · inspector {i.inspector ?? '—'} · <Link to={'/dams/' + i.damId}>dam</Link>{i.triggerAlertId && <> · raised from an alert</>}</p></div>
        <div className="row">
          {i.status === 'Planned' && can('manageInspections') && <button className="btn primary" onClick={() => run(() => api.post(`/inspections/${id}/start`), 'Inspection started.')}>Start inspection</button>}
          {live && <button className="btn green" onClick={() => setDone(true)}>Complete inspection</button>}
          {(i.status === 'Planned' || i.status === 'In progress') && can('manageInspections') && <button className="btn" onClick={() => run(() => api.post(`/inspections/${id}/cancel`), 'Inspection cancelled.')}>Cancel</button>}
        </div></div>
      <ErrorBox error={err} />
      {i.status === 'Completed' && <div className="card" style={{ marginBottom: 14 }}><div className="kv"><span>Completed</span><b>{fmtTime(i.completedAt)}</b><span>Overall condition</span><b>{i.overallCondition}</b><span>Summary</span><b>{i.summary ?? '—'}</b></div></div>}
      <div className="card"><h2>Checklist <span className="muted small">{answered} of {data.checklist.length} recorded{live && ' · record results, then Save'}</span>{live && <button className="btn primary sm" onClick={saveChecks}>Save checklist</button>}</h2>
        <div className="tablewrap"><table><thead><tr><th>Component</th><th>Check</th><th>Result</th><th>Remark</th></tr></thead>
          <tbody>{data.checklist.map((c) => { const v = checks[c.code] ?? { result: '', remark: '' }; return <tr key={c.code}><td className="small">{c.component}</td><td>{c.item}</td>
            <td>{live ? <select className="input" style={{ width: 'auto' }} value={v.result} onChange={(e) => setChecks({ ...checks, [c.code]: { ...v, result: e.target.value } })} aria-label={'Result ' + c.code}><option value="">—</option>{RESULTS.map((r) => <option key={r}>{r}</option>)}</select> : (c.result ? <span className={`badge ${c.result === 'OK' ? 'b-0' : c.result === 'Attention' ? 'b-1' : 'b-mute'}`}>{c.result}</span> : '—')}</td>
            <td>{live ? <input className="input" value={v.remark} onChange={(e) => setChecks({ ...checks, [c.code]: { ...v, remark: e.target.value } })} placeholder={v.result === 'Attention' ? 'Required' : ''} aria-label={'Remark ' + c.code} /> : <span className="small">{c.remark}</span>}</td></tr>; })}</tbody></table></div></div>
      <div className="card mt"><h2>Findings {live && <button className="btn primary sm" onClick={() => setAddF(true)}>+ Record finding</button>}</h2>
        <p className="muted small" style={{ marginTop: 0 }}>High findings raise a Level 2 alert and Critical findings a Level 3 alert (with notifications). Link the nearest instrument so its data can be checked.</p>
        <div className="tablewrap"><table><thead><tr><th>Severity</th><th>Finding</th><th>Component / location</th><th>Instrument</th><th>Due</th><th>Status</th><th></th></tr></thead>
          <tbody>{data.findings.map((f) => <tr key={f.id}><td><SeverityBadge s={f.severity} /></td><td><b>{f.description}</b>{f.recommendation && <div className="muted small">{f.recommendation}</div>}<FindingPhotos findingId={f.id} />{f.closureNote && <div className="small">Closed by {f.closedBy}: {f.closureNote}</div>}</td>
            <td>{f.component}<div className="muted small">{f.location}</div></td><td>{f.sensorId ? <Link to={'/sensors/' + f.sensorId} className="mono small">{f.sensorCode}</Link> : '—'}</td><td>{fmtDate(f.dueDate)}</td>
            <td>{f.status}{f.alertId && <div><Link to="/alerts" className="small">alert raised</Link></div>}</td>
            <td className="row">{f.status !== 'Closed' && can('manageInspections') && f.status !== 'Assigned' && <button className="btn sm" onClick={() => run(() => api.post(`/inspections/findings/${f.id}/status`, { status: 'Assigned' }), 'Finding assigned.')}>Assign</button>}
              {f.status !== 'Closed' && can('closeFindings') && <button className="btn sm" onClick={() => { setCloseF(f); setNote(''); }}>Close</button>}</td></tr>)}
            {data.findings.length === 0 && <tr><td colSpan={7} className="empty">No findings recorded.</td></tr>}</tbody></table></div></div>
      {addF && <Modal title="Record finding" onClose={() => setAddF(false)} footer={<><button className="btn" onClick={() => setAddF(false)}>Cancel</button><button className="btn primary" onClick={async () => { if (await run(() => api.post(`/inspections/${id}/findings`, { ...nf, sensorId: nf.sensorId || null }), nf.severity === 'High' || nf.severity === 'Critical' ? 'Finding recorded — alert raised.' : 'Finding recorded.')) { setAddF(false); setNf({ component: '', location: '', description: '', severity: 'Medium', sensorId: '', recommendation: '' }); } }}>Save</button></>}>
        <div className="form two">
          <Field label="Component"><select className="input" value={nf.component} onChange={(e) => setNf({ ...nf, component: e.target.value })}><option value="">—</option>{components.map((c) => <option key={c}>{c}</option>)}</select></Field>
          <Field label="Location"><input className="input" value={nf.location} onChange={(e) => setNf({ ...nf, location: e.target.value })} placeholder="e.g. Block 12 toe, chainage 0+250" /></Field>
          <Field label="Severity"><select className="input" value={nf.severity} onChange={(e) => setNf({ ...nf, severity: e.target.value })}>{SEVERITIES.map((s) => <option key={s}>{s}</option>)}</select></Field>
          <Field label="Nearest instrument"><select className="input" value={nf.sensorId} onChange={(e) => setNf({ ...nf, sensorId: e.target.value })}><option value="">—</option>{sensors.data?.map((s) => <option key={s.id} value={s.id}>{s.code} — {s.parameterName}</option>)}</select></Field>
        </div>
        <Field label="What was observed"><textarea className="input" value={nf.description} onChange={(e) => setNf({ ...nf, description: e.target.value })} /></Field>
        <Field label="Recommendation"><textarea className="input" value={nf.recommendation} onChange={(e) => setNf({ ...nf, recommendation: e.target.value })} /></Field>
        <p className="note">Due date is set automatically: Critical 1 day, High 7 days, Medium 30 days, Low 90 days.</p></Modal>}
      {done && <Modal title="Complete inspection" onClose={() => setDone(false)} footer={<><button className="btn" onClick={() => setDone(false)}>Cancel</button><button className="btn green" onClick={async () => { if (await run(() => api.post(`/inspections/${id}/complete`, cond), 'Inspection completed.')) setDone(false); }}>Complete</button></>}>
        <ErrorBox error={err} /><Field label="Overall condition"><select className="input" value={cond.overallCondition} onChange={(e) => setCond({ ...cond, overallCondition: e.target.value })}>{CONDITIONS.map((c) => <option key={c}>{c}</option>)}</select></Field>
        <Field label="Summary"><textarea className="input" value={cond.summary} onChange={(e) => setCond({ ...cond, summary: e.target.value })} /></Field>
        <p className="muted small">All checklist items must be recorded (and saved) before completion.</p></Modal>}
      {closeF && <Modal title="Close finding" onClose={() => setCloseF(null)} footer={<><button className="btn" onClick={() => setCloseF(null)}>Cancel</button><button className="btn primary" onClick={async () => { if (await run(() => api.post(`/inspections/findings/${closeF.id}/status`, { status: 'Closed', note }), 'Finding closed.')) setCloseF(null); }}>Close finding</button></>}>
        <ErrorBox error={err} /><p><b>{closeF.description}</b></p><Field label="What was done (required)"><textarea className="input" value={note} onChange={(e) => setNote(e.target.value)} /></Field></Modal>}
    </>
  );
}
