import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend, Filler } from 'chart.js';
import { Line } from 'react-chartjs-2';
import type { ReadingPoint } from '../api/types';
import { fmtTime } from '../api/client';

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Tooltip, Legend, Filler);

export default function ReadingsChart({ points, unit, alert, alarm, emergency }: { points: ReadingPoint[]; unit: string; alert?: number | null; alarm?: number | null; emergency?: number | null }) {
  const labels = points.map((p) => fmtTime(p.t));
  const line = (label: string, v: number | null | undefined, color: string) =>
    v === null || v === undefined ? [] : [{ label, data: points.map(() => v), borderColor: color, borderDash: [5, 4], pointRadius: 0, borderWidth: 1.3 }];
  return (
    <div className="chartbox">
      <Line
        data={{
          labels,
          datasets: [
            { label: unit ? `Reading (${unit})` : 'Reading', data: points.map((p) => (p.q === 'B' ? null : p.v)), borderColor: '#1f6fd1', pointRadius: 0, borderWidth: 1.7, spanGaps: true, tension: 0.15 },
            { label: 'Suspect (S)', data: points.map((p) => (p.q === 'S' ? p.v : null)), showLine: false, pointRadius: 4, pointBackgroundColor: '#d98a1c', borderColor: '#d98a1c' },
            ...line('Alert', alert, '#d98a1c'), ...line('Alarm', alarm, '#e06b1a'), ...line('Emergency', emergency, '#d14545'),
          ],
        }}
        options={{ responsive: true, maintainAspectRatio: false, animation: false, interaction: { mode: 'index', intersect: false },
          plugins: { legend: { position: 'bottom', labels: { boxWidth: 10 } } }, scales: { x: { ticks: { maxTicksLimit: 8 } }, y: { title: { display: true, text: unit } } } }}
      />
    </div>
  );
}
