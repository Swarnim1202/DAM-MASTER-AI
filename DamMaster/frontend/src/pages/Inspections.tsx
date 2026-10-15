import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, Finding, Inspection, MonsoonCompliance } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ComplianceBadge, InspectionBadge, SeverityBadge } from '../components/StatusBadges';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

export const INSPECTION_TYPES = ['Routine', 'Pre-monsoon', 'Post-monsoon', 'Special', 'Post-earthquake', 'Post-flood'];
const fmtDate = (d?: string | null) => (d ? new Date(d).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' }) : '—');
export { fmtDate };

export function PlanInspection({ damId, onClose, onSaved }: { damId?: string; onClose: () => void; onSaved: (id: string) => void }) {
  const dams = useApi<Dam[]>('/dams');
  const [f, setF] = useState({ damId: damId ?? '', inspectionType: 'Pre-monsoon', scheduledDate: new Date().toISOString().slice(0, 10), inspector: '' });
  const [err, setErr] = useState<string | null>(null);
  const save = async () => { try { const i = await api.post<Inspection>('/inspections', { ...f, damId: f.damId || dams.data?.[0]?.id }); onSaved(i.id); } catch (e) { setErr((e as Error).message); } };
  return (
    <Modal title="Plan inspection" onClose={onClose} footer={<><button className="btn" onClick={onClose}>Cancel</button><button className="btn primary" onClick={save}>Plan</button></>}>
      <ErrorBox error={err} />
      <div className="form two">
        {!damId && <Field label="Dam"><select className="input" value={f.damId} onChange={(e) => setF({ ...f, damId: e.target.value })}>{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>}
        <Field label="Type"><select className="input" value={f.inspectionType} onChange={(e) => setF({ ...f, inspectionType: e.target.value })}>{INSPECTION_TYPES.map((t) => <option key={t}>{t}</option>)}</select></Field>
        <Field label="Scheduled date"><input className="input" type="date" value={f.scheduledDate} onChange={(e) => setF({ ...f, scheduledDate: e.target.value })} /></Field>
        <Field label="Inspector (team leader)"><input className="input" value={f.inspector} onChange={(e) => setF({ ...f, inspector: e.target.value })} /></Field>
      </div>
      <p className="muted small">The Dam Safety Act requires a pre-monsoon and a post-monsoon inspection every year; special inspections follow earthquakes, floods and Level 2+ alerts.</p>
    </Modal>
  );
}

export function InspectionTable({ rows }: { rows: Inspection[] }) {
  const nav = useNavigate();
  return <div className="tablewrap"><table><thead><tr><th>Scheduled</th><th>Dam</th><th>Type</th><th>Status</th><th>Inspector</th><th>Checklist</th><th>Open findings</th><th>Condition</th></tr></thead>
    <tbody>{rows.map((i) => <tr key={i.id} className="click" onClick={() => nav('/inspections/' + i.id)}>
      <td>{fmtDate(i.scheduledDate)}{i.status !== 'Completed' && i.status !== 'Cancelled' && new Date(i.scheduledDate) < new Date(new Date().toISOString().slice(0, 10)) && <div><span className="badge b-3">overdue</span></div>}</td>
      <td>{i.damName}</td><td>{i.inspectionType}{i.triggerAlertId && <div className="muted small">from alert</div>}</td><td><InspectionBadge s={i.status} /></td><td>{i.inspector ?? '—'}</td><td>{i.checksDone}</td><td>{i.openFindings}</td><td>{i.overallCondition ?? '—'}</td></tr>)}
      {rows.length === 0 && <tr><td colSpan={8} className="empty">No inspections.</td></tr>}</tbody></table></div>;
}

export function FindingTable({ rows, showDam }: { rows: Finding[]; showDam?: boolean }) {
  return <div className="tablewrap"><table><thead><tr><th>Severity</th><th>Finding</th>{showDam && <th>Dam</th>}<th>Component / location</th><th>Instrument</th><th>Due</th><th>Status</th></tr></thead>
    <tbody>{rows.map((f) => <tr key={f.id}><td><SeverityBadge s={f.severity} /></td><td><Link to={'/inspections/' + f.inspectionId}><b>{f.description}</b></Link>{f.recommendation && <div className="muted small">{f.recommendation}</div>}</td>
      {showDam && <td>{f.damName}</td>}<td>{f.component}<div className="muted small">{f.location}</div></td><td className="mono small">{f.sensorCode ?? '—'}</td>
      <td>{fmtDate(f.dueDate)}{f.status !== 'Closed' && f.dueDate && new Date(f.dueDate) < new Date() && <div><span className="badge b-3">overdue</span></div>}</td><td>{f.status}{f.alertId && <div className="muted small">alert raised</div>}</td></tr>)}
      {rows.length === 0 && <tr><td colSpan={7} className="empty">No findings.</td></tr>}</tbody></table></div>;
}

export default function Inspections() {
  const { can } = useAuth(); const nav = useNavigate(); const toast = useToast();
  const [tab, setTab] = useState<'list' | 'findings' | 'compliance'>('list'); const [status, setStatus] = useState('Active'); const [year, setYear] = useState(new Date().getFullYear());
  const list = useApi<Inspection[]>(`/inspections?status=${status}`); const findings = useApi<Finding[]>('/inspections/findings?open=true'); const comp = useApi<MonsoonCompliance[]>(`/inspections/compliance?year=${year}`);
  const [plan, setPlan] = useState(false);
  return (
    <>
      <div className="hero"><div><h1>Inspections</h1><p>Plan and record inspections with a standard checklist, log findings against instruments, and track Dam Safety Act pre- and post-monsoon compliance.</p></div>
        {can('manageInspections') && <button className="btn primary" onClick={() => setPlan(true)}>+ Plan inspection</button>}</div>
      <div className="card">
        <div className="tabs" role="tablist">{([['list', 'Inspections'], ['findings', 'Open findings'], ['compliance', 'Monsoon compliance']] as const).map(([k, l]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>)}</div>
        {tab === 'list' && <><div className="row" style={{ marginBottom: 8 }}><select className="input" style={{ width: 'auto' }} value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status">{[['Active', 'Planned + in progress'], ['Completed', 'Completed'], ['Cancelled', 'Cancelled'], ['All', 'All']].map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
          <ErrorBox error={list.error} /><InspectionTable rows={list.data ?? []} /></>}
        {tab === 'findings' && <FindingTable rows={findings.data ?? []} showDam />}
        {tab === 'compliance' && <>
          <div className="row" style={{ marginBottom: 8 }}><span className="muted small">Year</span><select className="input" style={{ width: 'auto' }} value={year} onChange={(e) => setYear(Number(e.target.value))} aria-label="Year">{[0, 1, 2].map((k) => new Date().getFullYear() - k).map((y) => <option key={y}>{y}</option>)}</select>
            <span className="muted small">Due dates are configurable (appsettings → Inspection) — confirm with the State Dam Safety Organisation.</span></div>
          <div className="tablewrap"><table><thead><tr><th>Dam</th><th>Pre-monsoon (due)</th><th>Status</th><th>Post-monsoon (due)</th><th>Status</th><th>Open / overdue findings</th></tr></thead>
            <tbody>{comp.data?.map((c) => <tr key={c.damId}><td><b>{c.damName}</b></td><td>{fmtDate(c.preDue)}{c.preDone && <div className="muted small">done {fmtTime(c.preDone)}</div>}</td><td><ComplianceBadge s={c.preStatus} /></td>
              <td>{fmtDate(c.postDue)}{c.postDone && <div className="muted small">done {fmtTime(c.postDone)}</div>}</td><td><ComplianceBadge s={c.postStatus} /></td><td>{c.openFindings} / {c.overdueFindings}</td></tr>)}</tbody></table></div></>}
      </div>
      {plan && <PlanInspection onClose={() => setPlan(false)} onSaved={(id) => { setPlan(false); toast('Inspection planned.'); nav('/inspections/' + id); }} />}
    </>
  );
}
