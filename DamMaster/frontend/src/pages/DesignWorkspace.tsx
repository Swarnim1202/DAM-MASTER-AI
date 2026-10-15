import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Line } from 'react-chartjs-2';
import * as XLSX from 'xlsx';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { Dam, DesignCalc, DesignCatalog, DesignProjectDetail, DesignRecord } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import '../components/AnalyticsCharts';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

const ST: Record<string, string> = { Draft: 'b-mute', Checked: 'b-info', Approved: 'b-0' };

/** Cross-section of the selected dam (from the Main dam stability data). */
function Section({ d, frl, mwl }: { d: any; frl: number; mwl: number }) {
  if (!d) return null;
  const W = 760, H0 = 300, y0 = 250, s = Math.min(560 / d.b, 200 / d.h), x0 = (W - d.b * s) / 2, Y = (el: number) => y0 - (el - d.fl) * s, yc = Y(d.crest);
  const gravity = d.type === 'Concrete gravity'; const xb = gravity ? x0 : x0 + d.us * d.h * s; const ux = (el: number) => (gravity ? x0 : x0 + (el - d.fl) * d.us * s);
  return (
    <svg viewBox={`0 0 ${W} ${H0}`} style={{ width: '100%', background: 'var(--soft)', border: '1px solid var(--line)', borderRadius: 8 }} role="img" aria-label="Dam cross-section">
      <rect x={0} y={y0} width={W} height={H0 - y0} fill="rgba(140,110,70,.22)" /><line x1={0} y1={y0} x2={W} y2={y0} stroke="#8a6d45" />
      {gravity ? <><rect x={10} y={Y(frl)} width={x0 - 10} height={y0 - Y(frl)} fill="rgba(31,111,209,.2)" /><polygon points={`${x0},${y0} ${x0},${yc} ${x0 + d.t * s},${yc} ${x0 + d.b * s},${y0}`} fill="#b9c4cf" stroke="#546577" /></>
        : <><polygon points={`10,${Y(frl)} ${ux(frl)},${Y(frl)} ${x0},${y0} 10,${y0}`} fill="rgba(31,111,209,.2)" /><polygon points={`${x0},${y0} ${xb},${yc} ${xb + d.t * s},${yc} ${x0 + d.b * s},${y0}`} fill={d.kind === 'r' ? '#a7a39a' : '#c9b48c'} stroke="#6b5a3a" />
          {d.kind === 'r' && <line x1={x0} y1={y0} x2={xb} y2={yc} stroke="#546577" strokeWidth={4} />}</>}
      <line x1={10} y1={Y(mwl)} x2={ux(mwl)} y2={Y(mwl)} stroke="#d14545" strokeDasharray="5 3" />
      <text x={14} y={Y(mwl) - 4} fontSize={11} fill="#d14545">MWL {mwl}</text><text x={14} y={Y(frl) + 13} fontSize={11} fill="#1f6fd1">FRL {frl}</text>
      <text x={W / 2} y={yc - 8} fontSize={11} textAnchor="middle" fill="currentColor">Top {d.crest.toFixed(2)} · H {d.h.toFixed(1)} m</text>
      <text x={W / 2} y={y0 + 30} fontSize={11} textAnchor="middle" fill="currentColor">Base {d.b.toFixed(1)} m · {d.slopes}</text>
      <text x={W - 8} y={y0 + 16} fontSize={11} textAnchor="end" fill="#8a6d45">Foundation {d.fl.toFixed(1)}</text>
    </svg>
  );
}

function RecordModal({ projectId, code, onClose, onChanged }: { projectId: string; code: string; onClose: () => void; onChanged: () => void }) {
  const { data, error, reload } = useApi<{ calc: any; record: DesignRecord; codes: { code: string; title: string; verified: boolean }[]; history: { version: number; runBy: string; runAt: string }[] }>(`/design/projects/${projectId}/calcs/${code}`);
  const { can, user } = useAuth(); const toast = useToast(); const [err, setErr] = useState<string | null>(null);
  const act = async (a: 'check' | 'approve') => { try { await api.post(`/design/projects/${projectId}/calcs/${code}/${a}`); toast(a === 'check' ? 'Marked checked.' : 'Approved (Preliminary).'); setErr(null); void reload(); onChanged(); } catch (e) { setErr((e as Error).message); } };
  const c = data?.calc; const r = data?.record;
  return (
    <Modal title={r ? `${r.title} · v${c.version}` : 'Calculation record'} onClose={onClose} wide footer={<>
      {c && !c.stale && c.status === 'Draft' && can('checkDesign') && c.runBy !== user?.fullName && <button className="btn" onClick={() => act('check')}>Mark checked</button>}
      {c && !c.stale && c.status === 'Checked' && can('approveDesign') && <button className="btn green" onClick={() => act('approve')}>Approve (Preliminary)</button>}
      <button className="btn" disabled title="Issue for construction needs an authorised approver outside the software (FR-VC-03)">IFC 🔒</button><button className="btn" onClick={onClose}>Close</button></>}>
      <ErrorBox error={error || err} />
      {c && r && <div className="small">
        <div className="row" style={{ marginBottom: 8 }}><span className={`badge ${c.stale ? 'b-1' : 'b-info'}`}>{c.stale ? 'STALE' : 'Current'}</span><span className={`badge ${ST[c.status]}`}>{c.status}</span><span className="badge b-mute">{c.maturity}</span>
          <span className="muted">Run by {c.runBy} · {fmtTime(c.runAt)}{c.checkedBy && ` · checked by ${c.checkedBy}`}{c.approvedBy && ` · approved by ${c.approvedBy}`}</span></div>
        {c.stale && <p className="note">STALE: {c.staleReason}</p>}
        <h4>1 · Inputs</h4><div className="kv">{Object.entries(r.inputs).flatMap(([k, v]) => [<span key={k + 'k'}>{k}</span>, <b key={k + 'v'}>{v}</b>])}</div>
        <h4>2 · Assumptions</h4><ul>{r.assumptions.map((a, i) => <li key={i}>{a}</li>)}{r.assumptions.length === 0 && <li>None</li>}</ul>
        <h4>3 · Code / standard</h4><ul>{data!.codes.map((x) => <li key={x.code}>{x.title} — {x.verified ? <span className="badge b-0">Verified</span> : <span className="badge b-3">CODE VERIFICATION REQUIRED</span>}</li>)}{data!.codes.length === 0 && <li>None cited</li>}</ul>
        <h4>4 · Method</h4><p>{r.method}</p><h4>5 · Formula</h4><p className="mono">{r.formula}</p>
        <h4>6 · Calculation</h4><ol>{r.steps.map((s, i) => <li key={i} className="mono">{s}</li>)}</ol>
        <h4>7 · Results</h4><div className="kv">{r.results.flatMap((x) => [<span key={x.label}>{x.label}</span>, <b key={x.label + 'v'}>{x.value} {x.unit}</b>])}</div>
        <h4>8–10 · Limits, factors of safety, pass / fail</h4>{r.checks.length ? <table><thead><tr><th>Check</th><th>Computed</th><th>Limit</th><th>Result</th></tr></thead><tbody>{r.checks.map((k) => <tr key={k.label}><td>{k.label}</td><td>{k.value} {k.unit}</td><td>{k.max ? '≤ ' : '≥ '}{k.limit}</td><td><span className={`badge ${k.ok ? 'b-0' : 'b-3'}`}>{k.ok ? 'PASS' : 'FAIL'}</span></td></tr>)}</tbody></table> : <p className="muted">No pass / fail criteria.</p>}
        <h4>11 · Engineering interpretation</h4><p>{r.interpretation}</p>
        <h4>12 · Review requirement</h4><p>Independent check (Checker or Dam Safety Engineer, not the person who ran it), then approval at Preliminary maturity by an Approver who neither ran nor checked it. Approval is blocked until every cited standard is verified. Not for construction.</p>
        <h4>Versions</h4><p className="muted">{data!.history.map((h) => `v${h.version} by ${h.runBy}, ${fmtTime(h.runAt)}`).join(' · ')}</p></div>}
    </Modal>
  );
}

export default function DesignWorkspace() {
  const { id } = useParams(); const { can } = useAuth(); const toast = useToast();
  const cat = useApi<DesignCatalog>('/design/catalog'); const p = useApi<DesignProjectDetail>(`/design/projects/${id}`); const dams = useApi<Dam[]>('/dams');
  const impact = useApi<{ code: string; title: string; reason: string; path: string }[]>(`/design/projects/${id}/impact`);
  const changes = useApi<{ inputKey: string; oldValue: string; newValue: string; affected: string; changedBy: string; changedAt: string }[]>(`/design/projects/${id}/changes`);
  const [tab, setTab] = useState<'overview' | 'calcs' | 'inputs' | 'reservoir' | 'changes'>('overview');
  const [rec, setRec] = useState<string | null>(null); const [err, setErr] = useState<string | null>(null); const [pull, setPull] = useState(false); const [pullId, setPullId] = useState('');
  const [ea, setEa] = useState<number[][] | null>(null);
  const reloadAll = () => { void p.reload(); void impact.reload(); void changes.reload(); };
  if (!p.data || !cat.data) return <ErrorBox error={p.error || cat.error} />;
  const d = p.data; const C: Record<string, DesignCalc> = Object.fromEntries(d.calcs.map((c) => [c.code, c])); const stale = d.calcs.filter((c) => c.stale).length;
  const edit = can('editDesign');
  const setInput = async (key: string, raw: string, kind: string) => {
    try { const r = await api.put<{ changed: string[]; stale: string[] }>(`/design/projects/${id}/inputs`, { changes: { [key]: kind === 'number' ? Number(raw) : raw } }); setErr(null);
      if (r.changed.length) toast(r.stale.length ? `${r.stale.length} calculation(s) flagged STALE.` : 'Saved — no calculation depends on this input.'); reloadAll(); }
    catch (e) { setErr((e as Error).message); reloadAll(); }
  };
  const run = async (onlyStale: boolean) => { try { const r = await api.post<{ written: string[] }>(`/design/projects/${id}/run`, { onlyStale }); toast(r.written.length ? `Re-run: ${r.written.join(', ')}` : 'Nothing to run.'); reloadAll(); } catch (e) { setErr((e as Error).message); } };
  const doPull = async () => { try { const r = await api.post<{ note?: string }>(`/design/projects/${id}/pull-dam`, { damId: pullId || dams.data?.[0]?.id }); setPull(false); toast(r.note ?? 'Levels pulled from the dam register.'); reloadAll(); } catch (e) { setErr((e as Error).message); setPull(false); } };
  const exportXlsx = () => {
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['key', 'label', 'unit', 'group', 'value', 'allowed / note'], ...cat.data!.inputs.map((x) => [x.key, x.label, x.unit ?? '', x.group, d.inputs[x.key], x.options ? x.options.join(' | ') : x.help ?? ''])]), 'Design_Inputs');
    XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['elevation_m', 'area_km2'], ...d.ea]), 'Elevation_Area');
    XLSX.writeFile(wb, `DAM_MASTER_Design_Inputs_${d.project.code}.xlsx`);
  };
  const importXlsx = async (f: File) => {
    try {
      const wb = XLSX.read(await f.arrayBuffer(), { type: 'array' }); const rows = XLSX.utils.sheet_to_json<Record<string, unknown>>(wb.Sheets['Design_Inputs']);
      const changesIn: Record<string, unknown> = {}; for (const r of rows) if (r.key && r.value !== undefined && String(r.value) !== String(d.inputs[String(r.key)])) changesIn[String(r.key)] = r.value;
      const r1 = await api.put<{ stale: string[] }>(`/design/projects/${id}/inputs`, { changes: changesIn });
      const eaRows = wb.Sheets['Elevation_Area'] ? XLSX.utils.sheet_to_json<Record<string, number>>(wb.Sheets['Elevation_Area']).map((r) => [Number(r.elevation_m), Number(r.area_km2)]) : null;
      if (eaRows && eaRows.length) await api.put(`/design/projects/${id}/ea`, { rows: eaRows });
      toast(`${Object.keys(changesIn).length} input(s) imported; ${r1.stale.length} calculation(s) STALE.`); setErr(null); reloadAll();
    } catch (e) { setErr('Import rejected — nothing applied: ' + (e as Error).message); }
  };
  const groups = [...new Set(cat.data.inputs.map((x) => x.group))];
  const alt = C.type?.data?.alternatives ?? []; const route = C.route?.data?.series ?? [];
  const tabs: [typeof tab, string][] = [['overview', 'Overview'], ['calcs', `Calculations${stale ? ` (${stale} STALE)` : ''}`], ['inputs', 'Inputs'], ['reservoir', 'Reservoir & flood routing'], ['changes', 'Change log']];
  return (
    <>
      <div className="hero"><div><h1>{d.project.code} · {d.project.name}</h1><p>{d.project.damName ? <>Linked to <Link to={'/dams/' + d.project.damId}>{d.project.damName}</Link> · </> : ''}created by {d.project.createdBy} · all results are Preliminary maturity</p></div>
        <div className="row">{edit && <><button className={`btn ${stale ? 'green' : ''}`} onClick={() => run(true)}>↻ Recalculate STALE ({stale})</button><button className="btn" onClick={() => setPull(true)}>Pull levels from dam</button></>}
          <button className="btn" onClick={exportXlsx}>⤓ Inputs (.xlsx)</button>
          {edit && <label className="btn">⇪ Import inputs<input type="file" accept=".xlsx" hidden onChange={(e) => { const f = e.target.files?.[0]; if (f) void importXlsx(f); e.target.value = ''; }} /></label>}
          <button className="btn" onClick={() => window.print()}>Print</button></div></div>
      <ErrorBox error={err} />
      {stale > 0 && <div className="note" style={{ marginBottom: 12 }}><b>{stale} calculation(s) STALE</b> — results below are out of date until re-run.
        <ul style={{ margin: '6px 0 0' }}>{impact.data?.map((x) => <li key={x.code}><b>{x.title}</b>: {x.reason}{x.path && <span className="muted"> · {x.path}</span>}</li>)}</ul></div>}
      <div className="card"><div className="tabs" role="tablist">{tabs.map(([k, l]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>)}</div>
        {tab === 'overview' && <div className="grid g21">
          <div><h4 style={{ marginTop: 0 }}>Preliminary section · {C.dam?.data?.type}</h4><Section d={C.dam?.data} frl={Number(d.inputs.frl)} mwl={Number(d.inputs.mwl)} />
            <div className="tablewrap mt"><table><thead><tr><th>Alternative</th><th>Height</th><th>Base</th><th>Slopes / ratio</th><th>Cost (₹ cr)</th><th>Checks</th><th>Suitability</th></tr></thead>
              <tbody>{alt.map((a: any) => <tr key={a.type} style={a.type === C.type?.data?.selected ? { background: 'var(--bbg)' } : undefined}><td><b>{a.type}</b>{a.type === C.type?.data?.recommended && <span className="badge b-info" style={{ marginLeft: 6 }}>recommended</span>}</td><td>{a.h.toFixed(1)} m</td><td>{a.b.toFixed(1)} m</td><td>{a.slopes}</td>
                <td>{a.feasible ? (a.cost / 1e7).toFixed(0) : '—'}</td><td><span className={`badge ${!a.feasible ? 'b-3' : a.pass ? 'b-0' : 'b-1'}`}>{!a.feasible ? 'Not feasible' : a.pass ? 'Pass' : 'Fail'}</span></td><td><b>{a.score}</b>/100</td></tr>)}</tbody></table></div></div>
          <div><h4 style={{ marginTop: 0 }}>Engineering notes</h4>{d.calcs.filter((c) => c.code !== 'top').map((c) => <p key={c.code} className="small"><b>{c.title}.</b> {c.interpretation}{c.stale && <span className="badge b-1" style={{ marginLeft: 4 }}>STALE</span>}</p>)}</div></div>}
        {tab === 'calcs' && <div className="grid g3">{d.calcs.map((c) => { const fails = c.checks.filter((k) => !k.ok).length; return (
          <div key={c.code} className="card" style={c.stale ? { borderColor: 'var(--amber)', boxShadow: 'inset 3px 0 0 var(--amber)' } : undefined}>
            <h2 style={{ fontSize: 14 }}>{c.title}<span className="row" style={{ gap: 4 }}>{c.stale && <span className="badge b-1">STALE</span>}<span className={`badge ${ST[c.status]}`}>{c.status}</span></span></h2>
            <div className="kv">{c.results.slice(0, 5).flatMap((r) => [<span key={r.label}>{r.label}</span>, <b key={r.label + 'v'}>{r.value} {r.unit}</b>])}</div>
            <div className="row mt">{c.checks.length > 0 && <span className={`badge ${fails ? 'b-3' : 'b-0'}`}>{fails ? `${fails} check(s) FAIL` : `${c.checks.length} checks PASS`}</span>}
              {c.codes.some((x) => !x.verified) && <span className="badge b-3" title="Code edition / clauses not verified">CODE VERIFY</span>}<span className="muted small">v{c.version} · {c.runBy}</span></div>
            <button className="btn sm mt" onClick={() => setRec(c.code)}>Calculation record</button></div>); })}</div>}
        {tab === 'inputs' && <>{!edit && <p className="note">Read-only: only Design Engineers and Administrators change inputs.</p>}
          <p className="muted small" style={{ marginTop: 0 }}>Each change is checked for consistency (e.g. MWL ≥ FRL) and logged; dependent calculations are flagged STALE until re-run. Hover a label for guidance.</p>
          {groups.map((g) => <div key={g}><h4>{g}</h4><div className="form">{cat.data!.inputs.filter((x) => x.group === g).map((x) => <Field key={x.key} label={`${x.label}${x.unit ? ` (${x.unit})` : ''}`}>
            {x.kind === 'select' ? <select className="input" title={x.help ?? ''} disabled={!edit} value={String(d.inputs[x.key])} onChange={(e) => setInput(x.key, e.target.value, x.kind)}>{x.options!.map((o) => <option key={o}>{o}</option>)}</select>
              : <input className="input" title={x.help ?? ''} disabled={!edit} type={x.kind === 'number' ? 'number' : 'text'} step="any" defaultValue={String(d.inputs[x.key])} key={`${x.key}-${d.inputs[x.key]}`} onBlur={(e) => { if (e.target.value !== String(d.inputs[x.key])) void setInput(x.key, e.target.value, x.kind); }} />}</Field>)}</div></div>)}</>}
        {tab === 'reservoir' && <div className="grid g2">
          <div><h4 style={{ marginTop: 0 }}>Inflow, outflow and reservoir level</h4>{route.length > 0 && <div className="chartbox"><Line data={{ labels: route.map((r: number[]) => `${r[0].toFixed(1)} h`), datasets: [
            { label: 'Inflow (m³/s)', data: route.map((r: number[]) => Math.round(r[1])), borderColor: '#1597b8', pointRadius: 0, yAxisID: 'y' }, { label: 'Outflow (m³/s)', data: route.map((r: number[]) => Math.round(r[2])), borderColor: '#d98a1c', pointRadius: 0, yAxisID: 'y' },
            { label: 'Level (m)', data: route.map((r: number[]) => r[3]), borderColor: '#6a55e0', borderDash: [5, 3], pointRadius: 0, yAxisID: 'y1' }, { label: 'MWL', data: route.map(() => Number(d.inputs.mwl)), borderColor: '#d14545', borderDash: [2, 3], pointRadius: 0, yAxisID: 'y1' }] }}
            options={{ responsive: true, maintainAspectRatio: false, animation: false, interaction: { mode: 'index', intersect: false }, plugins: { legend: { position: 'bottom', labels: { boxWidth: 10 } } }, scales: { x: { ticks: { maxTicksLimit: 10 } }, y: { title: { display: true, text: 'm³/s' } }, y1: { position: 'right', grid: { display: false }, title: { display: true, text: 'm' } } } }} /></div>}
            <p className="small">{C.route?.interpretation}</p></div>
          <div><h4 style={{ marginTop: 0 }}>Elevation–area table</h4>
            <div className="tablewrap"><table><thead><tr><th>Elevation (m)</th><th>Area (km²)</th><th></th></tr></thead><tbody>{(ea ?? d.ea).map((r, k) => <tr key={k}>
              <td><input className="input" type="number" step="any" disabled={!edit} value={r[0]} onChange={(e) => { const t = (ea ?? d.ea).map((x) => [...x]); t[k][0] = Number(e.target.value); setEa(t); }} /></td>
              <td><input className="input" type="number" step="any" disabled={!edit} value={r[1]} onChange={(e) => { const t = (ea ?? d.ea).map((x) => [...x]); t[k][1] = Number(e.target.value); setEa(t); }} /></td>
              <td>{edit && <button className="btn sm" aria-label="Remove row" onClick={() => setEa((ea ?? d.ea).filter((_, j) => j !== k))}>✕</button>}</td></tr>)}</tbody></table></div>
            {edit && <div className="row mt"><button className="btn sm" onClick={() => { const t = ea ?? d.ea; const l = t[t.length - 1]; setEa([...t, [l[0] + 5, l[1] + 4]]); }}>+ Row</button>
              <button className="btn sm primary" disabled={!ea} onClick={async () => { try { const r = await api.put<{ stale: string[] }>(`/design/projects/${id}/ea`, { rows: ea }); setEa(null); setErr(null); toast(`Table saved; ${r.stale.length} calculation(s) STALE.`); reloadAll(); } catch (e) { setErr((e as Error).message); } }}>Save table</button>
              {ea && <button className="btn sm" onClick={() => setEa(null)}>Discard</button>}</div>}</div></div>}
        {tab === 'changes' && <div className="tablewrap"><table><thead><tr><th>When (IST)</th><th>Input</th><th>From → to</th><th>Calculations flagged</th><th>By</th></tr></thead>
          <tbody>{changes.data?.map((c, k) => <tr key={k}><td>{fmtTime(c.changedAt)}</td><td className="mono">{c.inputKey}</td><td>{c.oldValue} → {c.newValue}</td><td className="small">{c.affected || '—'}</td><td>{c.changedBy}</td></tr>)}
            {changes.data?.length === 0 && <tr><td colSpan={5} className="empty">No changes since the project was created.</td></tr>}</tbody></table></div>}
      </div>
      {rec && <RecordModal projectId={id!} code={rec} onClose={() => setRec(null)} onChanged={reloadAll} />}
      {pull && <Modal title="Pull levels from a registered dam" onClose={() => setPull(false)} footer={<><button className="btn" onClick={() => setPull(false)}>Cancel</button><button className="btn primary" onClick={doPull}>Pull</button></>}>
        <Field label="Dam"><select className="input" value={pullId} onChange={(e) => setPullId(e.target.value)}>{dams.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></Field>
        <p className="muted small">Takes FRL, MWL, MDDL, river bed (top of dam − height + foundation depth), seismic zone and dam type. Tailwater and spillway crest are set to placeholders and the elevation–area table is shifted — review them.</p></Modal>}
    </>
  );
}
