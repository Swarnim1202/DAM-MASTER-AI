import { useState } from 'react';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { DamDocument, DhiResult, DhiSnapshot } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { DhiHistoryChart } from '../components/AnalyticsCharts';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';
import { fmtDate } from './Inspections';
import { DocumentFiles } from './DamPhase6Tabs';

export const BAND_CLASS: Record<string, string> = { Good: 'b-0', Fair: 'b-info', Poor: 'b-1', Critical: 'b-3' };
export const RATING_CLASS: Record<string, string> = { Stable: 'b-0', Monitor: 'b-info', Watch: 'b-1', Action: 'b-3' };
const barColour = (s: number) => (s >= 85 ? '#12946a' : s >= 75 ? '#1f6fd1' : s >= 65 ? '#d98a1c' : '#d14545');

export function DhiTab({ damId }: { damId: string }) {
  const { data, error, reload } = useApi<{ current: DhiResult; history: DhiSnapshot[] }>(`/dams/${damId}/dhi`); const { can } = useAuth(); const toast = useToast();
  const c = data?.current;
  const snap = async () => { try { await api.post(`/dams/${damId}/dhi/snapshot`); toast('Snapshot recorded.'); void reload(); } catch (e) { toast((e as Error).message); } };
  return <>
    <ErrorBox error={error} />
    {c && <>
      <div className="grid g3">
        <div className="card kpi" style={{ borderTopColor: barColour(c.dhi) }}><small>Dam Health Index</small><strong>{c.dhi}</strong><span className={`badge ${BAND_CLASS[c.band]}`}>{c.band}</span></div>
        <div className="card kpi" style={{ borderTopColor: '#d14545' }}><small>Risk score</small><strong>{c.risk}</strong><span className={`badge ${RATING_CLASS[c.rating]}`}>{c.rating}</span></div>
        <div className="card"><div className="small">{c.riskReasons.map((r, i) => <div key={i} className="mono">{r}</div>)}</div><p className="muted small">Computed {fmtTime(c.computedAt)} · data as of {fmtTime(c.dataAsOf)}</p>
          {can('dhiSnapshot') && <button className="btn sm" onClick={snap}>Record snapshot</button>}</div>
      </div>
      <h4>Components (DHI = Σ weight × score)</h4>
      {c.components.map((x) => <div key={x.code} style={{ marginBottom: 14 }}>
        <div className="row" style={{ justifyContent: 'space-between' }}><b>{x.name}</b><span className="small">score <b>{x.score}</b> × weight {x.weight} = <b>{(x.score * x.weight).toFixed(1)}</b></span></div>
        <div style={{ height: 9, background: 'var(--soft)', borderRadius: 5, overflow: 'hidden', border: '1px solid var(--line)' }} role="img" aria-label={`${x.name} ${x.score} of 100`}><div style={{ width: `${x.score}%`, height: '100%', background: barColour(x.score) }} /></div>
        <ul className="small" style={{ margin: '4px 0 0', paddingLeft: 18, color: 'var(--muted)' }}>{x.reasons.map((r, i) => <li key={i}>{r}</li>)}</ul></div>)}
      <p className="muted small">Bands: Good ≥ 85 · Fair 75–85 · Poor 65–75 · Critical &lt; 65. Weights and scoring rules are in the reference library (DHI method) and await calibration by the dam safety team.</p>
      {data && data.history.length > 0 && <><h4>History</h4><DhiHistoryChart rows={data.history} /></>}
    </>}
  </>;
}

const TYPES = ['EAP', 'O&M manual', 'Instrument register', 'As-built drawings', 'Safety review', 'Inspection report', 'Design report', 'Other'];
const STATUSES = ['Approved', 'Under review', 'Revision due', 'Draft'];

export function DocumentsTab({ damId }: { damId: string }) {
  const { data, error, reload } = useApi<DamDocument[]>(`/dams/${damId}/documents`); const { can } = useAuth(); const toast = useToast();
  const [edit, setEdit] = useState<DamDocument | null>(null); const [err, setErr] = useState<string | null>(null);
  const save = async () => { try { const d = { ...edit!, issueDate: edit!.issueDate || null, nextReviewDate: edit!.nextReviewDate || null }; if (d.id) await api.put(`/dams/${damId}/documents/${d.id}`, d); else await api.post(`/dams/${damId}/documents`, d); setEdit(null); toast('Document saved.'); void reload(); } catch (e) { setErr((e as Error).message); } };
  const set = (k: keyof DamDocument, v: unknown) => setEdit({ ...edit!, [k]: v });
  const overdue = (d: DamDocument) => d.nextReviewDate && new Date(d.nextReviewDate) < new Date();
  return <>
    <ErrorBox error={error} />
    <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}><span className="muted small">Register of key documents (metadata). Approved documents within their review date feed the DHI documentation score.</span>
      {can('manageDocuments') && <button className="btn primary sm" onClick={() => { setEdit({ id: '', damId, docType: 'EAP', title: '', status: 'Approved' }); setErr(null); }}>+ Register document</button>}</div>
    <div className="tablewrap"><table><thead><tr><th>Type</th><th>Title</th><th>Reference</th><th>Issued</th><th>Next review</th><th>Status</th><th>Files</th><th></th></tr></thead>
      <tbody>{data?.map((d) => <tr key={d.id}><td>{d.docType}</td><td><b>{d.title}</b></td><td className="small">{d.referenceNo ?? '—'}</td><td>{fmtDate(d.issueDate)}</td>
        <td>{fmtDate(d.nextReviewDate)}{overdue(d) && <div><span className="badge b-3">review overdue</span></div>}</td><td><span className={`badge ${d.status === 'Approved' ? 'b-0' : d.status === 'Revision due' ? 'b-3' : 'b-1'}`}>{d.status}</span></td>
        <td><DocumentFiles damId={damId} documentId={d.id} canUpload={can('manageDocuments')} /></td>
        <td>{can('manageDocuments') && <button className="btn sm" onClick={() => { setEdit({ ...d, issueDate: d.issueDate?.slice(0, 10), nextReviewDate: d.nextReviewDate?.slice(0, 10) }); setErr(null); }}>Edit</button>}</td></tr>)}
        {data?.length === 0 && <tr><td colSpan={8} className="empty">No documents registered.</td></tr>}</tbody></table></div>
    {edit && <Modal title={edit.id ? 'Edit document' : 'Register document'} onClose={() => setEdit(null)} footer={<><button className="btn" onClick={() => setEdit(null)}>Cancel</button><button className="btn primary" onClick={save}>Save</button></>}>
      <ErrorBox error={err} /><div className="form two">
        <Field label="Type"><select className="input" value={edit.docType} onChange={(e) => set('docType', e.target.value)}>{TYPES.map((t) => <option key={t}>{t}</option>)}</select></Field>
        <Field label="Status"><select className="input" value={edit.status} onChange={(e) => set('status', e.target.value)}>{STATUSES.map((t) => <option key={t}>{t}</option>)}</select></Field>
        <Field label="Title"><input className="input" value={edit.title} onChange={(e) => set('title', e.target.value)} /></Field>
        <Field label="Reference number"><input className="input" value={edit.referenceNo ?? ''} onChange={(e) => set('referenceNo', e.target.value)} /></Field>
        <Field label="Issue date"><input className="input" type="date" value={edit.issueDate ?? ''} onChange={(e) => set('issueDate', e.target.value)} /></Field>
        <Field label="Next review date"><input className="input" type="date" value={edit.nextReviewDate ?? ''} onChange={(e) => set('nextReviewDate', e.target.value)} /></Field></div></Modal>}
  </>;
}
