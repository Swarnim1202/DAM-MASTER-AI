import { Chart as ChartJS, BarElement, CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend, Filler } from 'chart.js';
import { Bar, Line, Scatter } from 'react-chartjs-2';
import type { HstPoint, LagR } from '../api/types';
import { fmtTime } from '../api/client';

ChartJS.register(BarElement, CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend, Filler);
const base = { responsive: true, maintainAspectRatio: false, animation: false as const, plugins: { legend: { position: 'bottom' as const, labels: { boxWidth: 10 } } } };

export function HstFitChart({ pts, unit }: { pts: HstPoint[]; unit: string }) {
  return <div className="chartbox"><Line data={{ labels: pts.map((p) => fmtTime(p.t)), datasets: [
    { label: `Measured (${unit})`, data: pts.map((p) => p.measured), borderColor: '#1f6fd1', pointRadius: 0, borderWidth: 1.4 },
    { label: 'HST prediction', data: pts.map((p) => p.predicted), borderColor: '#d98a1c', pointRadius: 0, borderWidth: 1.6, borderDash: [6, 3] }] }}
    options={{ ...base, interaction: { mode: 'index', intersect: false }, scales: { x: { ticks: { maxTicksLimit: 8 } }, y: { title: { display: true, text: unit } } } }} /></div>;
}

export function ResidualChart({ pts, band }: { pts: HstPoint[]; band: number }) {
  return <div className="chartbox" style={{ height: 220 }}><Line data={{ labels: pts.map((p) => fmtTime(p.t)), datasets: [
    { label: 'Residual (measured − predicted)', data: pts.map((p) => p.residual), borderColor: '#6a55e0', pointRadius: pts.map((p) => (Math.abs(p.residual) > band ? 3 : 0)), pointBackgroundColor: '#d14545', borderWidth: 1.2 },
    { label: `+${band.toPrecision(3)} (k·σ)`, data: pts.map(() => band), borderColor: '#d14545', borderDash: [4, 4], pointRadius: 0, borderWidth: 1 },
    { label: `−${band.toPrecision(3)}`, data: pts.map(() => -band), borderColor: '#d14545', borderDash: [4, 4], pointRadius: 0, borderWidth: 1 }] }}
    options={{ ...base, scales: { x: { ticks: { maxTicksLimit: 8 } } } }} /></div>;
}

export function PartsChart({ pts, unit }: { pts: HstPoint[]; unit: string }) {
  return <div className="chartbox" style={{ height: 240 }}><Line data={{ labels: pts.map((p) => fmtTime(p.t)), datasets: [
    { label: 'Water-level effect (H)', data: pts.map((p) => p.hydro), borderColor: '#1597b8', pointRadius: 0, borderWidth: 1.5 },
    { label: 'Seasonal effect (S)', data: pts.map((p) => p.season), borderColor: '#12946a', pointRadius: 0, borderWidth: 1.5 },
    { label: 'Time / ageing effect (T)', data: pts.map((p) => p.time), borderColor: '#d14545', pointRadius: 0, borderWidth: 1.8 }] }}
    options={{ ...base, scales: { x: { ticks: { maxTicksLimit: 8 } }, y: { title: { display: true, text: unit } } } }} /></div>;
}

export function LagChart({ lags, label }: { lags: LagR[]; label: string }) {
  return <div className="chartbox" style={{ height: 200 }}><Bar data={{ labels: lags.map((l) => `${l.lagHours} h`), datasets: [{ label, data: lags.map((l) => l.r ?? 0), backgroundColor: lags.map((l) => (Math.abs(l.r ?? 0) >= 0.6 ? '#1f6fd1' : Math.abs(l.r ?? 0) >= 0.3 ? '#8fb3e6' : '#c9d3dd')) }] }}
    options={{ ...base, plugins: { legend: { display: false } }, scales: { y: { min: -1, max: 1, title: { display: true, text: 'correlation r' } }, x: { title: { display: true, text: 'lag' } } } }} /></div>;
}

export function ScatterChart({ pts, unit }: { pts: { h: number; v: number }[]; unit: string }) {
  return <div className="chartbox" style={{ height: 240 }}><Scatter data={{ datasets: [{ label: 'Last 90 days', data: pts.map((p) => ({ x: p.h, y: p.v })), backgroundColor: 'rgba(31,111,209,.45)', pointRadius: 2 }] }}
    options={{ ...base, plugins: { legend: { display: false } }, scales: { x: { title: { display: true, text: 'Reservoir level (m)' } }, y: { title: { display: true, text: unit } } } }} /></div>;
}

export function DhiHistoryChart({ rows }: { rows: { computedAt: string; dhi: number; risk: number }[] }) {
  return <div className="chartbox" style={{ height: 220 }}><Line data={{ labels: rows.map((r) => fmtTime(r.computedAt)), datasets: [
    { label: 'DHI', data: rows.map((r) => r.dhi), borderColor: '#12946a', pointRadius: 3 }, { label: 'Risk', data: rows.map((r) => r.risk), borderColor: '#d14545', pointRadius: 3, borderDash: [5, 3] }] }}
    options={{ ...base, scales: { y: { min: 0, max: 100 } } }} /></div>;
}
