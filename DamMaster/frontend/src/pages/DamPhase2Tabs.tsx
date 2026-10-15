import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { EapContact, Inspection, ScheduleRow } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ScheduleBadge } from '../components/StatusBadges';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';
import { InspectionTable, PlanInspection } from './Inspections';
import { useNavigate } from 'react-router-dom';

export function ScheduleTab({ damId }: { damId: string }) {
  const { data, error } = useApi<{ lifePhase: string; rows: ScheduleRow[] }>(`/dams/${damId}/schedule`);
  return <>
    <ErrorBox error={error} />
    <p className="muted small" style={{ marginTop: 0 }}>Required frequency for the dam's life phase <b>{data?.lifePhase}</b> (reference: REF-001 §7.2 — typical values, set per the dam's instrumentation report). Compliance counts valid readings in the 30 days before each sensor's last reading.</p>
    <div className="tablewrap"><table><thead><tr><th>Sensor</th><th>Required</th><th>Logger interval</th><th>Last reading (IST)</th><th>Next due</th><th>Status</th><th>30-day compliance</th></tr></thead>
      <tbody>{data?.rows.map((r) => <tr key={r.sensorId}><td className="mono"><Link to={'/sensors/' + r.sensorId}>{r.code}</Link></td>
        <td>{r.requiredHours ? `every ${r.requiredHours} h` : '—'}<div className="muted small">{r.note}</div></td>
        <td>{r.configuredMinutes / 60} h {r.loggerSlowerThanRequired && <span className="badge b-1">slower than required</span>}</td>
        <td>{fmtTime(r.lastReading)}</td><td>{fmtTime(r.nextDue)}</td><td><ScheduleBadge s={r.status} /></td>
        <td>{r.compliancePct != null ? <span className={`badge ${r.compliancePct >= 95 ? 'b-0' : r.compliancePct >= 80 ? 'b-1' : 'b-3'}`}>{r.compliancePct}%</span> : '—'} <span className="muted small">{r.readingsLast30d} readings</span></td></tr>)}</tbody></table></div>
  </>;
}

const blank = (damId: string): EapContact => ({ id: '', damId, name: '', organisation: '', eapRole: '', phone: '', email: '', minLevel: 2, escalationOrder: 1, isActive: true });

export function EapTab({ damId }: { damId: string }) {
  const { data, error, reload } = useApi<EapContact[]>(`/dams/${damId}/eap-contacts`); const { can } = useAuth(); const toast = useToast();
  const [edit, setEdit] = useState<EapContact | null>(null); const [err, setErr] = useState<string | null>(null);
  const save = async () => { try { if (edit!.id) await api.put(`/dams/${damId}/eap-contacts/${edit!.id}`, edit); else await api.post(`/dams/${damId}/eap-contacts`, edit); setEdit(null); toast('Contact saved.'); void reload(); } catch (e) { setErr((e as Error).message); } };
  const set = (k: keyof EapContact, v: unknown) => setEdit({ ...edit!, [k]: v });
  return <>
    <ErrorBox error={error} />
    <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}><span className="muted small">Order 1 contacts are told at once (Level ≥ their threshold, SMS + e-mail). Higher orders are told only if the alert is not acknowledged in time — one order per escalation step.</span>
      {can('manageEap') && <button className="btn primary sm" onClick={() => { setEdit(blank(damId)); setErr(null); }}>+ Add contact</button>}</div>
    <div className="tablewrap"><table><thead><tr><th>Order</th><th>Name</th><th>Organisation / EAP role</th><th>Phone</th><th>E-mail</th><th>From level</th><th>Status</th><th></th></tr></thead>
      <tbody>{data?.map((e) => <tr key={e.id}><td>{e.escalationOrder}</td><td><b>{e.name}</b></td><td>{e.organisation}<div className="muted small">{e.eapRole}</div></td><td className="mono small">{e.phone ?? '—'}</td><td className="small">{e.email ?? '—'}</td><td>Level {e.minLevel}+</td>
        <td><span className={`badge ${e.isActive ? 'b-0' : 'b-mute'}`}>{e.isActive ? 'Active' : 'Inactive'}</span></td><td>{can('manageEap') && <button className="btn sm" onClick={() => { setEdit(e); setErr(null); }}>Edit</button>}</td></tr>)}
        {data?.length === 0 && <tr><td colSpan={8} className="empty">No EAP contacts — Level 2 and 3 alerts will only reach system users.</td></tr>}</tbody></table></div>
    {edit && <Modal title={edit.id ? 'Edit EAP contact' : 'Add EAP contact'} onClose={() => setEdit(null)} footer={<><button className="btn" onClick={() => setEdit(null)}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
      <ErrorBox error={err} /><div className="form two">
        <Field label="Name"><input className="input" value={edit.name} onChange={(e) => set('name', e.target.value)} /></Field>
        <Field label="Organisation"><input className="input" value={edit.organisation ?? ''} onChange={(e) => set('organisation', e.target.value)} /></Field>
        <Field label="Role in the EAP"><input className="input" value={edit.eapRole ?? ''} onChange={(e) => set('eapRole', e.target.value)} placeholder="e.g. Evacuation authority" /></Field>
        <Field label="Phone (for SMS)"><input className="input" value={edit.phone ?? ''} onChange={(e) => set('phone', e.target.value)} placeholder="+91 98xxxxxxxx" /></Field>
        <Field label="E-mail"><input className="input" type="email" value={edit.email ?? ''} onChange={(e) => set('email', e.target.value)} /></Field>
        <Field label="Notify from level"><select className="input" value={edit.minLevel} onChange={(e) => set('minLevel', Number(e.target.value))}>{[1, 2, 3].map((l) => <option key={l} value={l}>Level {l} and above</option>)}</select></Field>
        <Field label="Escalation order"><input className="input" type="number" min={1} max={9} value={edit.escalationOrder} onChange={(e) => set('escalationOrder', Number(e.target.value) || 1)} /></Field>
        <Field label="Status"><select className="input" value={edit.isActive ? 'Active' : 'Inactive'} onChange={(e) => set('isActive', e.target.value === 'Active')}><option>Active</option><option>Inactive</option></select></Field>
      </div></Modal>}
  </>;
}

export function InspectionsTab({ damId }: { damId: string }) {
  const { data } = useApi<Inspection[]>(`/inspections?damId=${damId}&status=All`); const { can } = useAuth(); const nav = useNavigate();
  const [plan, setPlan] = useState(false);
  return <>
    <div className="row" style={{ justifyContent: 'flex-end', marginBottom: 8 }}>{can('manageInspections') && <button className="btn primary sm" onClick={() => setPlan(true)}>+ Plan inspection</button>}</div>
    <InspectionTable rows={data ?? []} />
    {plan && <PlanInspection damId={damId} onClose={() => setPlan(false)} onSaved={(id) => nav('/inspections/' + id)} />}
  </>;
}
