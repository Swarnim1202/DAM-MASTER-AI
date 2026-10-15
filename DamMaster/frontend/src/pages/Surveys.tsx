import { useState } from 'react';
import * as XLSX from 'xlsx';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, SurveyDataset } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

interface Cmp { from: { name: string; epochDate: string }; to: { name: string; epochDate: string }; years: number; matched: number; onlyInFirst: number; onlyInSecond: number; maxSettlementMm: number; maxHeaveMm: number; rows: { pointId: string; label?: string; from: number; to: number; changeMm: number; ratePerYear: number }[] }

export default function Surveys() {
  const { data, error, reload } = useApi<SurveyDataset[]>('/surveys'); const dams = useApi<Dam[]>('/dams'); const { can } = useAuth(); const toast = useToast();
  const [imp, setImp] = useState(false); const [f, setF] = useState({ damId: '', kind: 'InSAR', name: '', provider: '', epochDate: '', periodFrom: '', periodTo: '', threshold: '10' });
  const [file, setFile] = useState<File | null>(null); const [err, setErr] = useState<string | null>(null);
  const [a, setA] = useState(''); const [b, setB] = useState(''); const [cmp, setCmp] = useState<Cmp | null>(null);
  const surveys = (data ?? []).filter((x) => x.kind === 'Survey');
  const doImport = async () => {
    try {
      if (!file) throw new Error('Choose a CSV or Excel file.');
      const wb = XLSX.read(await file.arrayBuffer(), { type: 'array' }); const rows = XLSX.utils.sheet_to_json<Record<string, unknown>>(wb.Sheets[wb.SheetNames[0]]);
      const pick = (r: Record<string, unknown>, ...k: string[]) => { const key = Object.keys(r).find((x) => k.includes(x.trim().toLowerCase())); return key ? r[key] : undefined; };
      const points = rows.map((r) => ({ pointId: String(pick(r, 'point_id', 'pointid', 'id') ?? ''), latitude: Number(pick(r, 'latitude', 'lat')), longitude: Number(pick(r, 'longitude', 'lon', 'lng')),
        value: Number(pick(r, 'velocity_mm_yr', 'velocity', 'vel', 'value', 'elevation_m', 'elevation', 'height')), coherence: pick(r, 'coherence') != null ? Number(pick(r, 'coherence')) : null, label: pick(r, 'label', 'name') != null ? String(pick(r, 'label', 'name')) : null }));
      const r = await api.post<{ pointCount: number; alert?: { title: string } }>('/surveys', { ...f, damId: f.damId || dams.data?.[0]?.id, threshold: Number(f.threshold), epochDate: f.epochDate || null, periodFrom: f.periodFrom || null, periodTo: f.periodTo || null, points });
      setImp(false); setFile(null); toast(`${r.pointCount} points imported.${r.alert ? ' Alert raised: ' + r.alert.title : ''}`); void reload();
    } catch (e) { setErr((e as Error).message); }
  };
  const compare = async () => { try { setCmp(await api.get<Cmp>(`/surveys/compare?a=${a}&b=${b}`)); } catch (e) { toast((e as Error).message); } };
  return (
    <>
      <div className="hero"><div><h1>Remote sensing &amp; surveys</h1><p>InSAR ground-movement products (velocity in mm/year; points beyond the threshold raise a Level 1 alert) and survey campaigns (drone, levelling, total station, GNSS) compared epoch to epoch.</p></div>
        {can('manageSurveys') && <button className="btn primary" onClick={() => { setImp(true); setErr(null); }}>+ Import dataset</button>}</div>
      <ErrorBox error={error} />
      <div className="card"><div className="tablewrap"><table><thead><tr><th>Type</th><th>Name</th><th>Dam</th><th>Period / epoch</th><th>Points</th><th>Threshold</th><th>Imported</th></tr></thead>
        <tbody>{data?.map((d) => <tr key={d.id}><td><span className={`badge ${d.kind === 'InSAR' ? 'b-info' : 'b-mute'}`}>{d.kind}</span></td><td><b>{d.name}</b><div className="muted small">{d.provider}</div></td><td>{d.damName}</td>
          <td>{d.kind === 'Survey' ? d.epochDate?.slice(0, 10) : `${d.periodFrom?.slice(0, 10) ?? '—'} → ${d.periodTo?.slice(0, 10) ?? '—'}`}</td><td>{d.pointCount}</td><td>{d.threshold != null ? `${d.threshold} ${d.unit}` : '—'}</td><td>{fmtTime(d.createdAt)}<div className="muted small">{d.createdBy}</div></td></tr>)}
          {data?.length === 0 && <tr><td colSpan={7} className="empty">No datasets imported.</td></tr>}</tbody></table></div></div>
      <div className="card mt"><h2>Compare survey campaigns</h2>
        <div className="row"><select className="input" style={{ width: 'auto' }} value={a} onChange={(e) => setA(e.target.value)} aria-label="First campaign"><option value="">First campaign…</option>{surveys.map((s) => <option key={s.id} value={s.id}>{s.damName} · {s.name} ({s.epochDate?.slice(0, 10)})</option>)}</select>
          <select className="input" style={{ width: 'auto' }} value={b} onChange={(e) => setB(e.target.value)} aria-label="Second campaign"><option value="">Second campaign…</option>{surveys.map((s) => <option key={s.id} value={s.id}>{s.damName} · {s.name} ({s.epochDate?.slice(0, 10)})</option>)}</select>
          <button className="btn primary" disabled={!a || !b} onClick={compare}>Compare</button></div>
        {cmp && <><p className="small mt">{cmp.from.name} ({cmp.from.epochDate.slice(0, 10)}) → {cmp.to.name} ({cmp.to.epochDate.slice(0, 10)}): {cmp.years} year(s), {cmp.matched} matched points ({cmp.onlyInFirst} only in first, {cmp.onlyInSecond} only in second). Largest settlement <b>{cmp.maxSettlementMm} mm</b>, largest heave {cmp.maxHeaveMm} mm.</p>
          <div className="tablewrap" style={{ maxHeight: 360 }}><table><thead><tr><th>Point</th><th>Label</th><th>From (m)</th><th>To (m)</th><th>Change (mm)</th><th>Rate (mm/year)</th></tr></thead>
            <tbody>{cmp.rows.map((r) => <tr key={r.pointId}><td className="mono">{r.pointId}</td><td>{r.label}</td><td>{r.from.toFixed(4)}</td><td>{r.to.toFixed(4)}</td><td><span className={`badge ${r.changeMm <= -10 ? 'b-3' : r.changeMm <= -5 ? 'b-1' : 'b-0'}`}>{r.changeMm}</span></td><td>{r.ratePerYear}</td></tr>)}</tbody></table></div></>}</div>
      {imp && <Modal title="Import dataset" onClose={() => setImp(false)} footer={<><button className="btn" onClick={() => setImp(false)}>Cancel</button><button className="btn primary" onClick={doImport}>Import</button></>}>
        <ErrorBox error={err} /><div className="form two">
          <Field label="Dam"><select className="input" value={f.damId} onChange={(e) => setF({ ...f, damId: e.target.value })}>{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
          <Field label="Type"><select className="input" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}><option value="InSAR">InSAR velocity (mm/year)</option><option value="Survey">Survey campaign (elevation, m)</option></select></Field>
          <Field label="Name"><input className="input" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={f.kind === 'InSAR' ? 'Sentinel-1 2024–2026' : 'Crest levelling March 2026'} /></Field>
          <Field label="Provider"><input className="input" value={f.provider} onChange={(e) => setF({ ...f, provider: e.target.value })} /></Field>
          {f.kind === 'InSAR' ? <><Field label="Period from"><input className="input" type="date" value={f.periodFrom} onChange={(e) => setF({ ...f, periodFrom: e.target.value })} /></Field><Field label="Period to"><input className="input" type="date" value={f.periodTo} onChange={(e) => setF({ ...f, periodTo: e.target.value })} /></Field>
            <Field label="Alert threshold (mm/year)"><input className="input" type="number" value={f.threshold} onChange={(e) => setF({ ...f, threshold: e.target.value })} /></Field></>
            : <Field label="Survey (epoch) date"><input className="input" type="date" value={f.epochDate} onChange={(e) => setF({ ...f, epochDate: e.target.value })} /></Field>}
          <Field label="File (CSV / Excel)"><input className="input" type="file" accept=".csv,.xlsx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></Field></div>
        <p className="muted small">Columns: <span className="mono">point_id, latitude, longitude, {f.kind === 'InSAR' ? 'velocity_mm_yr, coherence' : 'elevation_m, label'}</span>. Point ids must be unique; campaigns are matched by point id.</p></Modal>}
    </>
  );
}
