import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { useNavigate } from 'react-router-dom';
import { useApi } from '../api/hooks';
import { ErrorBox } from '../components/ui';

interface MapData { tileUrl: string; attribution: string; dams: { id: string; code: string; name: string; lat?: number | null; lon?: number | null; maxOpenLevel: number; openAlerts: number; dhi?: number | null; dhiBand?: string | null }[];
  sensors: { id: string; code: string; parameterName: string; damId: string; lat: number; lon: number; latestValue?: number | null; engUnit: string; level: number }[];
  insar: { damId: string; dataset: string; threshold?: number | null; points: { pointId: string; lat: number; lon: number; v: number }[] }[] }
const LEVEL = ['#12946a', '#d98a1c', '#e06b1a', '#d14545'];
const vColour = (v: number, t: number) => (Math.abs(v) > t ? '#d14545' : Math.abs(v) > t / 2 ? '#d98a1c' : '#2f9e6a');

export default function MapPage() {
  const { data, error } = useApi<MapData>('/map'); const ref = useRef<HTMLDivElement>(null); const map = useRef<L.Map | null>(null); const nav = useNavigate();
  const [layers, setLayers] = useState({ sensors: true, insar: true });
  useEffect(() => {
    if (!data || !ref.current) return;
    map.current?.remove(); const m = L.map(ref.current, { preferCanvas: true }); map.current = m;
    L.tileLayer(data.tileUrl, { attribution: data.attribution, maxZoom: 19 }).addTo(m);
    const pts: L.LatLngExpression[] = [];
    for (const d of data.dams.filter((x) => x.lat != null && x.lon != null)) {
      const ll: L.LatLngExpression = [Number(d.lat), Number(d.lon)]; pts.push(ll);
      L.circleMarker(ll, { radius: 11, color: '#0a1d2e', weight: 2, fillColor: LEVEL[d.maxOpenLevel] ?? '#12946a', fillOpacity: 0.9 }).addTo(m)
        .bindTooltip(`<b>${d.name}</b><br/>DHI ${d.dhi ?? '—'} (${d.dhiBand ?? '—'}) · ${d.openAlerts} open alert(s)`).on('click', () => nav('/dams/' + d.id));
    }
    if (layers.insar) for (const g of data.insar) for (const p of g.points) L.circleMarker([p.lat, p.lon], { radius: 3, stroke: false, fillColor: vColour(p.v, g.threshold ?? 10), fillOpacity: 0.85 }).addTo(m).bindTooltip(`${p.pointId}: ${p.v.toFixed(1)} mm/year (${g.dataset})`);
    if (layers.sensors) for (const s of data.sensors) L.circleMarker([s.lat, s.lon], { radius: 6, color: '#ffffff', weight: 1.5, fillColor: LEVEL[s.level] ?? '#1f6fd1', fillOpacity: 1 }).addTo(m)
      .bindTooltip(`<b>${s.code}</b> ${s.parameterName}<br/>${s.latestValue ?? '—'} ${s.engUnit}`).on('click', () => nav('/sensors/' + s.id));
    if (pts.length) m.fitBounds(L.latLngBounds(pts).pad(pts.length === 1 ? 0.02 : 0.2)); else m.setView([22.5, 79], 5);
    if (pts.length === 1) m.setZoom(14);
    return () => { m.remove(); map.current = null; };
  }, [data, layers, nav]);
  const unlocated = data?.dams.filter((d) => d.lat == null || d.lon == null) ?? [];
  return (
    <>
      <div className="hero"><div><h1>Map</h1><p>Dams coloured by highest open alert level, instruments with coordinates, and InSAR ground-movement points (green ≤ half the threshold, amber, red &gt; threshold). Click a dam or sensor to open it.</p></div>
        <div className="row"><label className="small"><input type="checkbox" checked={layers.sensors} onChange={(e) => setLayers({ ...layers, sensors: e.target.checked })} /> Sensors</label>
          <label className="small"><input type="checkbox" checked={layers.insar} onChange={(e) => setLayers({ ...layers, insar: e.target.checked })} /> InSAR points</label></div></div>
      <ErrorBox error={error} />
      {unlocated.length > 0 && <p className="note">No coordinates yet for: {unlocated.map((d) => d.name).join(', ')} — set latitude / longitude on the dam.</p>}
      <div className="card" style={{ padding: 0, overflow: 'hidden' }}><div ref={ref} style={{ height: '70vh', width: '100%' }} aria-label="Map of dams, sensors and InSAR points" /></div>
      <p className="muted small">Base map tiles come from the configured tile server (Map:TileUrl). On closed networks point it to an internal tile server.</p>
    </>
  );
}
