import { useState } from 'react';
import { api, fmtNum, fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { HstResponse, ResponseResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { HstFitChart, LagChart, PartsChart, ResidualChart, ScatterChart } from '../components/AnalyticsCharts';
import { ErrorBox, Field, Modal, useToast } from '../components/ui';

const TERM_LABEL: Record<string, string> = { a0: 'Constant', z: 'Level z', z2: 'Level z²', z3: 'Level z³', z4: 'Level z⁴', sin1: 'sin s (annual)', cos1: 'cos s (annual)', sin2: 'sin 2s (half-year)', cos2: 'cos 2s (half-year)', t: 'Time τ (per year)', lnt: 'ln(1+τ)' };
const ST: Record<string, string> = { Active: 'b-0', Draft: 'b-1', Superseded: 'b-mute', Rejected: 'b-3' };

export default function SensorAnalytics({ sensorId, unit, parameterCode }: { sensorId: string; unit: string; parameterCode: string }) {
  const { can, user } = useAuth(); const toast = useToast();
  const [tab, setTab] = useState<'hst' | 'response'>('hst'); const [k, setK] = useState('2.5'); const [busy, setBusy] = useState(false); const [err, setErr] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<string | null>(null); const [note, setNote] = useState('');
  const [modelId, setModelId] = useState<string | null>(null);
  const hst = useApi<HstResponse>(`/sensors/${sensorId}/hst${modelId ? '?modelId=' + modelId : ''}`);
  const resp = useApi<ResponseResult>(tab === 'response' ? `/sensors/${sensorId}/response` : null);
  const isLoad = ['RWL', 'TWL', 'INF', 'RNF', 'GTO', 'ACC', 'ATM'].includes(parameterCode);
  const m = hst.data?.shown; const pts = hst.data?.series ?? [];
  const act = async (fn: () => Promise<unknown>, msg: string) => { setBusy(true); try { await fn(); setErr(null); toast(msg); setModelId(null); void hst.reload(); } catch (e) { setErr((e as Error).message); } finally { setBusy(false); } };
  return (
    <div className="card mt">
      <div className="tabs" role="tablist"><button role="tab" aria-selected={tab === 'hst'} className={tab === 'hst' ? 'on' : ''} onClick={() => setTab('hst')}>Behaviour model (HST)</button><button role="tab" aria-selected={tab === 'response'} className={tab === 'response' ? 'on' : ''} onClick={() => setTab('response')}>Reservoir &amp; rainfall response</button></div>
      <ErrorBox error={err || hst.error} />
      {tab === 'hst' && (isLoad ? <p className="muted">Behaviour models are fitted to response instruments (seepage, pressure, movement, strain). This is a load or event sensor.</p> : <>
        <p className="muted small" style={{ marginTop: 0 }}>The model learns how this instrument normally responds to <b>water level (H)</b>, <b>season (S)</b> and <b>time (T)</b>. Once approved, alerts compare each reading with the model's prediction (residual beyond k·σ for the persistence count) instead of the simple 14-day band. Under ~11 months of data a provisional H+T model is fitted.</p>
        {can('fitModels') && <div className="row" style={{ marginBottom: 10 }}><span className="small">Residual limit k</span><input className="input" style={{ width: 80 }} type="number" step="0.1" min={1.5} max={5} value={k} onChange={(e) => setK(e.target.value)} aria-label="Residual limit k" />
          <button className="btn primary sm" disabled={busy} onClick={() => act(() => api.post(`/sensors/${sensorId}/hst/fit`, { kLimit: Number(k) }), 'Model fitted — awaiting approval.')}>{busy ? 'Fitting…' : 'Fit new model'}</button></div>}
        {hst.data && hst.data.models.length > 0 && <div className="tablewrap"><table><thead><tr><th>Fitted</th><th>Variant</th><th>Data</th><th>R²</th><th>σ</th><th>k</th><th>Drift / year</th><th>Status</th><th></th></tr></thead>
          <tbody>{hst.data.models.map((x) => <tr key={x.id} style={x.id === m?.id ? { background: 'var(--bbg)' } : undefined}><td>{fmtTime(x.fittedAt)}<div className="muted small">{x.fittedBy}</div></td><td>{x.variant}</td>
            <td className="small">{fmtTime(x.dataFrom)} – {fmtTime(x.dataTo)}<div className="muted">{x.nPoints} points</div></td><td>{x.r2.toFixed(3)}</td><td>{fmtNum(x.sigma, 3)}</td><td>{x.kLimit}</td><td>{fmtNum(x.driftPerYear, 3)} {unit}</td>
            <td><span className={`badge ${ST[x.status]}`}>{x.status}</span>{x.approvedBy && <div className="muted small">{x.approvedBy}</div>}</td>
            <td className="row"><button className="btn sm" onClick={() => setModelId(x.id)}>View</button>
              {x.status === 'Draft' && can('approveThresholds') && x.fittedBy !== user?.fullName && <><button className="btn sm green" onClick={() => act(() => api.post(`/sensors/${sensorId}/hst/${x.id}/approve`), 'Model approved and active.')}>Approve</button><button className="btn sm" onClick={() => { setRejecting(x.id); setNote(''); }}>Reject</button></>}</td></tr>)}</tbody></table></div>}
        {m && <>
          {m.notes && <p className="note mt">{m.notes}</p>}
          <div className="grid g21 mt"><div><h4 style={{ margin: '0 0 6px' }}>Measured vs predicted ({m.variant}, R² {m.r2.toFixed(3)})</h4><HstFitChart pts={pts} unit={unit} /></div>
            <div><h4 style={{ margin: '0 0 6px' }}>Coefficients</h4><div className="tablewrap"><table><tbody>{Object.entries(hst.data?.coefficients ?? {}).map(([t, v]) => <tr key={t}><td className="small">{TERM_LABEL[t] ?? t}</td><td className="mono">{fmtNum(v, 4)}</td></tr>)}</tbody></table></div>
              <p className="muted small">z = (level − {fmtNum(m.hMin)}) / ({fmtNum(m.hMax)} − {fmtNum(m.hMin)}); s = 2π·day/365.25; τ = years since {fmtTime(m.t0)}.</p></div></div>
          <div className="grid g2 mt"><div><h4 style={{ margin: '0 0 6px' }}>Residual and alert band (±{m.kLimit}·σ)</h4><ResidualChart pts={pts} band={m.kLimit * m.sigma} /></div>
            <div><h4 style={{ margin: '0 0 6px' }}>What drives the reading</h4><PartsChart pts={pts} unit={unit} />
              <p className="muted small">A rising time effect is irreversible behaviour (creep, swelling, drain clogging, erosion) that seasonal swings would otherwise hide. Current drift: <b>{fmtNum(m.driftPerYear, 3)} {unit}/year</b>.</p></div></div></>}
        {hst.data && hst.data.models.length === 0 && <p className="muted">No model yet.</p>}
      </>)}
      {tab === 'response' && <>{resp.error && <ErrorBox error={resp.error} />}
        {resp.data?.reservoir && <div className="grid g2"><div><h4 style={{ margin: '0 0 6px' }}>Correlation with reservoir level at different lags</h4><LagChart lags={resp.data.reservoir.lags} label="r with reservoir level" /><p className="small">{resp.data.reservoir.interpretation}</p></div>
          <div><h4 style={{ margin: '0 0 6px' }}>Reading vs reservoir level</h4><ScatterChart pts={resp.data.reservoir.scatter} unit={unit} /><p className="muted small">Slope ≈ {fmtNum(resp.data.reservoir.slopePerMetre, 3)} {unit} per metre.</p></div></div>}
        {resp.data && !resp.data.reservoir && <p className="muted">No reservoir-level sensor with data on this dam (or this is the reservoir sensor).</p>}
        {resp.data?.rain && <div className="grid g2 mt"><div><h4 style={{ margin: '0 0 6px' }}>Change in reading vs rain in the previous 24 h ({resp.data.rain.gauge})</h4><LagChart lags={resp.data.rain.lags} label="r with rainfall" /><p className="small">{resp.data.rain.interpretation}</p></div>
          <div><h4 style={{ margin: '0 0 6px' }}>Rain days ≥ {resp.data.rain.eventThresholdMm} mm and the change in the next 72 h</h4><div className="tablewrap" style={{ maxHeight: 240 }}><table><thead><tr><th>Day (IST)</th><th>Rain</th><th>Max rise</th></tr></thead>
            <tbody>{resp.data.rain.events.map((e) => <tr key={e.day}><td>{new Date(e.day).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' })}</td><td>{fmtNum(e.mm, 1)} mm</td><td>{e.rise == null ? '—' : `${fmtNum(e.rise, 3)} ${unit}`}</td></tr>)}
              {resp.data.rain.events.length === 0 && <tr><td colSpan={3} className="empty">No rain days above the threshold.</td></tr>}</tbody></table></div></div></div>}
      </>}
      {rejecting && <Modal title="Reject model" onClose={() => setRejecting(null)} footer={<><button className="btn" onClick={() => setRejecting(null)}>Cancel</button><button className="btn danger" onClick={async () => { const id = rejecting; setRejecting(null); await act(() => api.post(`/sensors/${sensorId}/hst/${id}/reject`, { note }), 'Model rejected.'); }}>Reject</button></>}>
        <Field label="Reason"><textarea className="input" value={note} onChange={(e) => setNote(e.target.value)} /></Field></Modal>}
    </div>
  );
}
