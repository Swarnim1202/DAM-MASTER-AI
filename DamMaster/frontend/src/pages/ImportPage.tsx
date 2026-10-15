import { useState } from 'react';
import * as XLSX from 'xlsx';
import { api, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { ImportResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Kpi, LevelBadge, useToast } from '../components/ui';

type Row = Record<string, unknown>;
const camel = (k: string) => k.trim().toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '').replace(/_([a-z0-9])/g, (_, c: string) => c.toUpperCase());
const pad = (n: number) => String(n).padStart(2, '0');
/** Excel serial date (IST wall-clock) → ISO string with +05:30 offset. */
const serialToIst = (s: number, dateOnly = false) => {
  const d = new Date(Math.round((s - 25569) * 86400000));
  const day = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
  return dateOnly ? day : `${day}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:00+05:30`;
};
const TEXT = new Set(['damId', 'damName', 'state', 'damType', 'hazardClass', 'seismicZone', 'sensorId', 'parameterCode', 'instrumentType', 'location', 'engUnit', 'direction', 'status', 'unit', 'loggerStatus', 'observedAt', 'calibrationDue']);

function sheetRows(wb: XLSX.WorkBook, name: string): Row[] | null {
  const sn = wb.SheetNames.find((s) => camel(s) === camel(name));
  if (!sn) return null;
  return XLSX.utils.sheet_to_json<Row>(wb.Sheets[sn], { defval: null, raw: true }).map((r) => {
    const o: Row = {};
    for (const [k, v0] of Object.entries(r)) {
      const k2 = camel(k); let v = v0;
      if (v === null || v === '') { o[k2] = null; continue; }
      if (k2 === 'observedAt' && typeof v === 'number') v = serialToIst(v);
      else if (k2 === 'calibrationDue' && typeof v === 'number') v = serialToIst(v, true);
      else if (TEXT.has(k2)) v = String(v).trim();
      o[k2] = v;
    }
    return o;
  }).filter((r) => Object.values(r).some((v) => v !== null));
}

export default function ImportPage() {
  const { can } = useAuth(); const toast = useToast();
  const [payload, setPayload] = useState<{ fileName: string; dams: Row[]; sensors: Row[]; readings: Row[] } | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null); const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false);
  const [filter, setFilter] = useState('All');
  const batches = useApi<Row[]>('/import/batches'); const quarantine = useApi<Row[]>('/import/quarantine');

  const readFile = async (f: File) => {
    setErr(null); setResult(null);
    try {
      const isCsv = /\.csv$/i.test(f.name);
      const wb = XLSX.read(await f.arrayBuffer(), { type: 'array', raw: isCsv });
      const readings = isCsv ? XLSX.utils.sheet_to_json<Row>(wb.Sheets[wb.SheetNames[0]], { defval: null }).map((r) => Object.fromEntries(Object.entries(r).map(([k, v]) => [camel(k), v === null ? null : TEXT.has(camel(k)) ? String(v) : v]))) : sheetRows(wb, 'Readings');
      const p = { fileName: f.name, dams: isCsv ? [] : sheetRows(wb, 'Dam_Master') ?? [], sensors: isCsv ? [] : sheetRows(wb, 'Sensor_Master') ?? [], readings: readings ?? [] };
      if (!p.dams.length && !p.sensors.length && !p.readings.length) { setErr('No Dam_Master, Sensor_Master or Readings sheet found. Use the DAM MASTER template.'); return; }
      setPayload(p); await send(p, false);
    } catch (e) { setErr('Could not read the file: ' + (e as Error).message); }
  };
  const send = async (p = payload, commit = false) => {
    if (!p) return; setBusy(true); setErr(null);
    try {
      const r = await api.post<ImportResult>('/import', { ...p, commit });
      setResult(r);
      if (commit) { toast(`Import committed: ${r.good + r.suspect + r.bad} readings saved, ${r.alertsRaised.length} alert(s).`); setPayload(null); void batches.reload(); void quarantine.reload(); }
    } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };
  const issues = (result?.issues ?? []).filter((i) => filter === 'All' || i.outcome === filter);
  return (
    <>
      <div className="hero"><div><h1>Import instrument data</h1><p>Upload the DAM MASTER Excel template (Dam_Master, Sensor_Master, Readings) or a CSV of readings. The server validates every row first; nothing is saved until you commit.</p></div></div>
      {!can('importData') && <div className="note" style={{ marginBottom: 12 }}>Your role can view import history but cannot import data.</div>}
      <ErrorBox error={err} />
      {can('importData') && <div className="drop" onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); const f = e.dataTransfer.files[0]; if (f) void readFile(f); }}>
        <b>Drop the .xlsx / .csv file here</b><br /><br />
        <label className="btn primary">Choose file<input type="file" accept=".xlsx,.xls,.csv" hidden onChange={(e) => { const f = e.target.files?.[0]; if (f) void readFile(f); e.target.value = ''; }} /></label>
        <p className="muted small">observed_at is IST. Unknown sensors are quarantined; out-of-range or logger-fault readings are kept with flag B.</p></div>}
      {busy && <p className="muted">Validating on the server…</p>}
      {result && <div className="card mt">
        <h2>{result.committed ? 'Import committed' : `Validation result — ${payload?.fileName ?? ''}`}<span className="muted small">{result.damsUpserted} dam(s), {result.sensorsUpserted} sensor(s) in file</span></h2>
        <div className="grid g5">
          <Kpi label="Rows read" value={result.rowsRead} colour="#8193a5" /><Kpi label="Good (G)" value={result.good} colour="#12946a" /><Kpi label="Suspect (S)" value={result.suspect} colour="#d98a1c" />
          <Kpi label="Bad (B)" value={result.bad} colour="#d14545" /><Kpi label="Rejected / quarantined / duplicate" value={`${result.rejected} / ${result.quarantined} / ${result.duplicates}`} colour="#6a55e0" />
        </div>
        {!result.committed && <div className="row mt"><button className="btn green" disabled={busy || result.good + result.suspect + result.bad + result.damsUpserted + result.sensorsUpserted === 0} onClick={() => send(payload, true)}>✓ Commit import</button><button className="btn" onClick={() => { setResult(null); setPayload(null); }}>Cancel</button></div>}
        {result.committed && result.alertsRaised.length > 0 && <><h4>Alerts raised</h4><ul>{result.alertsRaised.map((a) => <li key={a.id}><LevelBadge level={a.level} /> {a.title}</li>)}</ul></>}
        <div className="row mt"><span className="muted small">Issues:</span><select className="input" style={{ width: 'auto' }} value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Filter issues">{['All', 'Rejected', 'Quarantined', 'Skipped', 'Flag B', 'Flag S', 'Warning'].map((x) => <option key={x}>{x}</option>)}</select></div>
        <div className="tablewrap mt" style={{ maxHeight: 360 }}><table><thead><tr><th>Sheet</th><th>Row</th><th>Item</th><th>Outcome</th><th>Rule</th><th>Detail</th></tr></thead>
          <tbody>{issues.slice(0, 1000).map((i, k) => <tr key={k}><td>{i.sheet}</td><td>{i.rowNo}</td><td className="mono">{i.itemCode}</td><td>{i.outcome}</td><td>{i.ruleCode}</td><td>{i.message}</td></tr>)}
            {issues.length === 0 && <tr><td colSpan={6} className="empty">No issues of this type.</td></tr>}</tbody></table></div></div>}
      <div className="grid g2 mt">
        <div className="card"><h2>Import history</h2><div className="tablewrap" style={{ maxHeight: 320 }}><table><thead><tr><th>When (IST)</th><th>Source</th><th>File</th><th>G / S / B</th><th>Rej / Q / Dup</th><th>By</th></tr></thead>
          <tbody>{(batches.data ?? []).map((b) => <tr key={String(b.id)}><td>{fmtTime(String(b.created_at))}</td><td>{String(b.source)}</td><td className="small">{String(b.file_name ?? '')}</td><td>{String(b.good)} / {String(b.suspect)} / {String(b.bad)}</td><td>{String(b.rejected)} / {String(b.quarantined)} / {String(b.duplicates)}</td><td>{String(b.created_by)}</td></tr>)}</tbody></table></div></div>
        <div className="card"><h2>Quarantine {can('importData') && (quarantine.data?.length ?? 0) > 0 && <button className="btn sm danger" onClick={async () => { await api.del('/import/quarantine'); void quarantine.reload(); toast('Quarantine cleared.'); }}>Discard all</button>}</h2>
          <p className="muted small" style={{ marginTop: 0 }}>Readings for sensors not yet registered. Register the sensor (Sensor_Master) and re-import the readings.</p>
          <div className="tablewrap" style={{ maxHeight: 280 }}><table><thead><tr><th>Sensor</th><th>Dam</th><th>Observed</th><th>Value</th></tr></thead>
            <tbody>{(quarantine.data ?? []).map((q) => <tr key={String(q.id)}><td className="mono">{String(q.sensorCode)}</td><td>{String(q.damCode)}</td><td>{fmtTime(q.observedAt as string)}</td><td>{String(q.value)} {String(q.unit ?? '')}</td></tr>)}
              {quarantine.data?.length === 0 && <tr><td colSpan={4} className="empty">Empty.</td></tr>}</tbody></table></div></div>
      </div>
    </>
  );
}
