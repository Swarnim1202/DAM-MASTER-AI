import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { DesignProjectRow, DocFile, ThresholdSuggestion, Twin } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, useToast } from '../components/ui';

const LEVEL = ['#12946a', '#d98a1c', '#e06b1a', '#d14545'];

/** Live section: dam outline, reservoir and tailwater at their latest levels, instruments at their real positions coloured by alert level. */
export function TwinTab({ damId }: { damId: string }) {
  const { data, error } = useApi<Twin>(`/dams/${damId}/twin`);
  if (!data) return <ErrorBox error={error} />;
  const g = data.geometry; const gravity = !g.kind; const W = 900, H0 = 380, y0 = 320;
  const xUs = gravity ? 0 : g.us * g.h; const totalW = Math.max(g.b, xUs + g.t + g.ds * g.h) + 60;
  const s = Math.min((W - 160) / totalW, 260 / g.h); const x0 = 100; const X = (x: number) => x0 + x * s; const Y = (el: number) => y0 - (el - g.fl) * s;
  const top = Y(g.crest); const res = data.reservoirLevel ?? data.dam.frlM ?? null; const tw = data.tailwaterLevel;
  const usFace = (el: number) => (gravity ? X(0) : X((el - g.fl) * g.us));
  const placed = data.sensors.filter((x) => x.elevationM != null && x.offsetM != null);
  return (
    <>
      <p className="muted small" style={{ marginTop: 0 }}>{data.source}. Instruments are placed by elevation and offset from the heel; colour = highest open alert level. {data.unplaced > 0 && `${data.unplaced} sensor(s) have no elevation / offset yet.`}</p>
      <svg viewBox={`0 0 ${W} ${H0}`} style={{ width: '100%', background: 'var(--soft)', border: '1px solid var(--line)', borderRadius: 8 }} role="img" aria-label="Live dam section">
        <rect x={0} y={y0} width={W} height={H0 - y0} fill="rgba(140,110,70,.25)" />
        {res != null && <><polygon points={`10,${Y(res)} ${usFace(res)},${Y(res)} ${usFace(g.fl)},${y0} 10,${y0}`} fill="rgba(31,111,209,.25)" /><text x={14} y={Y(res) - 5} fontSize={12} fill="#1f6fd1">Reservoir {fmtNum(res)} m</text></>}
        {data.dam.mwlM != null && <><line x1={10} y1={Y(Number(data.dam.mwlM))} x2={usFace(Number(data.dam.mwlM))} y2={Y(Number(data.dam.mwlM))} stroke="#d14545" strokeDasharray="5 3" /><text x={14} y={Y(Number(data.dam.mwlM)) - 4} fontSize={10} fill="#d14545">MWL {data.dam.mwlM}</text></>}
        {gravity ? <polygon points={`${X(0)},${y0} ${X(0)},${top} ${X(g.t)},${top} ${X(g.b)},${y0}`} fill="#b9c4cf" stroke="#546577" />
          : <polygon points={`${X(0)},${y0} ${X(xUs)},${top} ${X(xUs + g.t)},${top} ${X(xUs + g.t + g.ds * g.h)},${y0}`} fill={g.kind === 'r' ? '#a7a39a' : '#c9b48c'} stroke="#6b5a3a" />}
        {tw != null && <rect x={X(g.b)} y={Y(tw)} width={W - X(g.b) - 10} height={y0 - Y(tw)} fill="rgba(31,111,209,.18)" />}
        <text x={X(g.b / 2)} y={top - 8} fontSize={11} textAnchor="middle" fill="currentColor">Top {fmtNum(g.crest)} m · {g.type}</text>
        {placed.map((p, i) => { const ly = Y(p.elevationM!) + (i % 2 === 0 ? -12 : 18); return <g key={p.id}><circle cx={X(p.offsetM!)} cy={Y(p.elevationM!)} r={7} fill={LEVEL[p.level] ?? '#1f6fd1'} stroke="#fff" strokeWidth={2} />
          <line x1={X(p.offsetM!)} y1={Y(p.elevationM!)} x2={X(p.offsetM!) + 14} y2={ly - 4} stroke="#8193a5" strokeWidth={0.8} />
          <text x={X(p.offsetM!) + 16} y={ly} fontSize={10.5} fill="currentColor">{p.code}: {fmtNum(p.latest)} {p.unit}</text></g>; })}
      </svg>
      <div className="tablewrap mt"><table><thead><tr><th>Sensor</th><th>Elevation</th><th>Offset from heel</th><th>Chainage</th><th>Latest</th><th>Time (IST)</th><th>Open level</th></tr></thead>
        <tbody>{data.sensors.map((x) => <tr key={x.id}><td className="mono"><Link to={'/sensors/' + x.id}>{x.code}</Link></td><td>{x.elevationM ?? '—'}</td><td>{x.offsetM ?? '—'}</td><td>{x.chainageM ?? '—'}</td><td>{fmtNum(x.latest)} {x.unit}</td><td>{fmtTime(x.latestAt)}</td><td><span className={`badge b-${x.level}`}>{x.level ? `Level ${x.level}` : 'Normal'}</span></td></tr>)}</tbody></table></div>
    </>
  );
}

/** Threshold suggestions from an approved design project. */
export function DesignLinkTab({ damId }: { damId: string }) {
  const projects = useApi<DesignProjectRow[]>('/design/projects'); const { can } = useAuth(); const toast = useToast();
  const [pid, setPid] = useState(''); const own = (projects.data ?? []).filter((p) => p.damId === damId); const id = pid || own[0]?.id || '';
  const sg = useApi<{ project: { code: string; name: string }; damCalc: { version: number; status: string; stale: boolean }; canApply: boolean; note: string; rows: ThresholdSuggestion[] }>(id ? `/dams/${damId}/design-thresholds?projectId=${id}` : null);
  const [sel, setSel] = useState<string[]>([]); const [err, setErr] = useState<string | null>(null);
  const apply = async () => { try { const r = await api.post<{ applied: number; note: string }>(`/dams/${damId}/design-thresholds`, { projectId: id, sensorIds: sel }); toast(`${r.applied} threshold version(s) created. ${r.note}`); setSel([]); setErr(null); } catch (e) { setErr((e as Error).message); } };
  if (projects.data && own.length === 0) return <p className="muted">No design project is linked to this dam. Create one in the Design workspace and use “Pull levels from dam”.</p>;
  return <>
    <div className="row" style={{ marginBottom: 8 }}><span className="small">Design project</span><select className="input" style={{ width: 'auto' }} value={id} onChange={(e) => setPid(e.target.value)} aria-label="Design project">{own.map((p) => <option key={p.id} value={p.id}>{p.code} · {p.name}</option>)}</select>
      {sg.data && <span className={`badge ${sg.data.canApply ? 'b-0' : 'b-1'}`}>Main dam stability v{sg.data.damCalc.version} · {sg.data.damCalc.status}{sg.data.damCalc.stale ? ' · STALE' : ''}</span>}</div>
    <ErrorBox error={err || sg.error} />{sg.data && <p className="note">{sg.data.note}</p>}
    <div className="tablewrap"><table><thead><tr><th></th><th>Sensor</th><th>Alert</th><th>Alarm</th><th>Emergency</th><th>Basis</th></tr></thead>
      <tbody>{sg.data?.rows.map((r) => <tr key={r.sensorId}><td>{r.ok && <input type="checkbox" aria-label={'Select ' + r.code} checked={sel.includes(r.sensorId)} onChange={() => setSel(sel.includes(r.sensorId) ? sel.filter((x) => x !== r.sensorId) : [...sel, r.sensorId])} />}</td>
        <td className="mono">{r.code}</td><td>{r.alert ?? '—'}</td><td>{r.alarm ?? '—'}</td><td>{r.emergency ?? '—'} {r.ok && r.unit}</td><td className="small">{r.basis}</td></tr>)}</tbody></table></div>
    {can('applyDesignThresholds') && <button className="btn primary mt" disabled={!sg.data?.canApply || sel.length === 0} onClick={apply}>Create threshold versions for {sel.length} sensor(s)</button>}
    <p className="muted small">Applied values become new threshold versions (method “Design”, basis citing the project and calculation version) and still need approval by another Dam Safety Engineer or an Approver.</p>
  </>;
}

/** Files attached to a registered document: upload (indexed for search) and download. */
export function DocumentFiles({ damId, documentId, canUpload }: { damId: string; documentId: string; canUpload: boolean }) {
  const { data, reload } = useApi<DocFile[]>(`/dams/${damId}/documents/${documentId}/files`); const toast = useToast(); const [busy, setBusy] = useState(false);
  const upload = async (f: File) => {
    setBusy(true); const fd = new FormData(); fd.append('file', f);
    try { const r = await fetch(`/api/dams/${damId}/documents/${documentId}/files`, { method: 'POST', body: fd, credentials: 'include', headers: { 'X-Requested-With': 'DamMaster' } }); const j = await r.json();
      if (!r.ok) throw new Error(j.error ?? 'Upload failed'); toast(j.extractedOk ? `${j.fileName}: ${j.textChars.toLocaleString('en-IN')} characters indexed for search.` : `${j.fileName} stored. ${j.extractNote}`); void reload(); }
    catch (e) { toast((e as Error).message); } finally { setBusy(false); }
  };
  return <div className="small">{data?.map((f) => <div key={f.id}><a href={`/api/files/${f.id}`}>{f.fileName}</a> <span className="muted">({Math.round(f.sizeBytes / 1024)} KB{f.pages ? `, ${f.pages} pages` : ''}{f.extractedOk ? ', searchable' : ` — ${f.extractNote}`})</span></div>)}
    {canUpload && <label className="btn sm" style={{ marginTop: 4 }}>{busy ? 'Uploading…' : '⇪ Upload file'}<input type="file" hidden accept=".pdf,.docx,.txt,.md,.csv,.html,.htm" disabled={busy} onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f); e.target.value = ''; }} /></label>}</div>;
}
