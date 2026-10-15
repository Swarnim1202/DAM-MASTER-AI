import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, DesignProjectRow } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

export default function DesignProjects() {
  const { data, error } = useApi<DesignProjectRow[]>('/design/projects'); const dams = useApi<Dam[]>('/dams'); const { can } = useAuth(); const nav = useNavigate(); const toast = useToast();
  const [add, setAdd] = useState(false); const [f, setF] = useState({ name: '', damId: '', copyFromId: '' }); const [err, setErr] = useState<string | null>(null);
  const create = async () => { try { const r = await api.post<{ id: string; code: string }>('/design/projects', { name: f.name, damId: f.damId || null, copyFromId: f.copyFromId || null }); toast(`Project ${r.code} created — all nine calculations run.`); nav('/design/' + r.id); } catch (e) { setErr((e as Error).message); } };
  return (
    <>
      <div className="hero"><div><h1>Design workspace</h1><p>Preliminary dam design: one set of inputs drives nine connected calculations — top of dam, dam type, stability, spillway, flood routing, stilling basin, gates, hydropower and cost. Changing an input flags every dependent calculation STALE; results are checked and approved by different people, and approval needs verified code references.</p></div>
        {can('editDesign') && <button className="btn primary" onClick={() => { setAdd(true); setErr(null); }}>+ New design project</button>}</div>
      <ErrorBox error={error} />
      <div className="card"><div className="tablewrap"><table><thead><tr><th>Code</th><th>Project</th><th>Dam</th><th>Calculations</th><th>STALE</th><th>Approved</th><th>Status</th><th>Updated (IST)</th></tr></thead>
        <tbody>{data?.map((p) => <tr key={p.id} className="click" onClick={() => nav('/design/' + p.id)}><td className="mono">{p.code}</td><td><b>{p.name}</b><div className="muted small">by {p.createdBy}</div></td><td>{p.damName ?? '—'}</td><td>{p.calcCount}</td>
          <td>{p.staleCount ? <span className="badge b-1">{p.staleCount} STALE</span> : <span className="badge b-0">current</span>}</td><td>{p.approvedCount} / {p.calcCount}</td><td>{p.status}</td><td>{fmtTime(p.updatedAt ?? p.createdAt)}</td></tr>)}
          {data?.length === 0 && <tr><td colSpan={8} className="empty">No design projects yet.</td></tr>}</tbody></table></div></div>
      {add && <Modal title="New design project" onClose={() => setAdd(false)} footer={<><button className="btn" onClick={() => setAdd(false)}>Cancel</button><button className="btn primary" onClick={create}>Create</button></>}>
        <ErrorBox error={err} /><Field label="Project name"><input className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="e.g. River Valley Dam — spillway augmentation" /></Field>
        <div className="form two"><Field label="Linked dam (optional)"><select className="input" value={f.damId} onChange={(e) => setF({ ...f, damId: e.target.value })}><option value="">—</option>{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
          <Field label="Start from"><select className="input" value={f.copyFromId} onChange={(e) => setF({ ...f, copyFromId: e.target.value })}><option value="">Default inputs</option>{data?.map((p) => <option key={p.id} value={p.id}>Copy of {p.code} · {p.name}</option>)}</select></Field></div></Modal>}
    </>
  );
}
