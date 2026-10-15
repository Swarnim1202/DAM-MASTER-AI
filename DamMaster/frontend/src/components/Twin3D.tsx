import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { useApi } from '../api/hooks';
import type { Twin } from '../api/types';
import { ErrorBox } from './ui';

const LEVEL = [0x12946a, 0xd98a1c, 0xe06b1a, 0xd14545];

/** 3-D dam: the design section extruded along the crest length, reservoir at its current level, instruments at (offset, elevation, chainage). */
export default function Twin3D({ damId }: { damId: string }) {
  const { data, error } = useApi<Twin & { dam: { crestLengthM?: number | null } }>(`/dams/${damId}/twin`); const ref = useRef<HTMLDivElement>(null); const [fail, setFail] = useState<string | null>(null);
  useEffect(() => {
    if (!data || !ref.current) return;
    const el = ref.current; let renderer: THREE.WebGLRenderer;
    try { renderer = new THREE.WebGLRenderer({ antialias: true }); } catch { setFail('3-D view needs WebGL, which this browser or device does not provide.'); return; }
    const g = data.geometry; const L = Number(data.dam.crestLengthM ?? 300); const W = el.clientWidth, H = 460;
    renderer.setSize(W, H); renderer.setPixelRatio(Math.min(2, window.devicePixelRatio)); el.appendChild(renderer.domElement);
    const scene = new THREE.Scene(); scene.background = new THREE.Color(0xeef3f7);
    const cam = new THREE.PerspectiveCamera(45, W / H, 1, 20000); const span = Math.max(L, g.b * 3); cam.position.set(g.b + span * 0.55, g.h * 1.6 + span * 0.3, -span * 0.45);
    scene.add(new THREE.HemisphereLight(0xffffff, 0x8a7a60, 1.1)); const sun = new THREE.DirectionalLight(0xffffff, 1.2); sun.position.set(-200, 400, 300); scene.add(sun);
    // section in (x = downstream from heel, y = height above foundation), extruded along z = crest length
    const gravity = !g.kind; const s = new THREE.Shape(); const xUs = gravity ? 0 : g.us * g.h;
    if (gravity) { s.moveTo(0, 0); s.lineTo(0, g.h); s.lineTo(g.t, g.h); s.lineTo(g.b, 0); } else { s.moveTo(0, 0); s.lineTo(xUs, g.h); s.lineTo(xUs + g.t, g.h); s.lineTo(xUs + g.t + g.ds * g.h, 0); }
    s.closePath();
    const dam = new THREE.Mesh(new THREE.ExtrudeGeometry(s, { depth: L, bevelEnabled: false }), new THREE.MeshStandardMaterial({ color: gravity ? 0xb9c4cf : g.kind === 'r' ? 0xa7a39a : 0xc9b48c, roughness: 0.9 }));
    scene.add(dam);
    const ground = new THREE.Mesh(new THREE.BoxGeometry(g.b + 6 * g.h, 2, L * 1.4), new THREE.MeshStandardMaterial({ color: 0x9c8a6a })); ground.position.set(g.b / 2, -1, L / 2); scene.add(ground);
    const res = data.reservoirLevel ?? data.dam.frlM;
    if (res != null) {
      const depth = Number(res) - g.fl; const w = 3 * g.h;
      const water = new THREE.Mesh(new THREE.BoxGeometry(w, depth, L), new THREE.MeshStandardMaterial({ color: 0x1f6fd1, transparent: true, opacity: 0.45 }));
      water.position.set(-w / 2 + (gravity ? 0 : xUs * 0.15), depth / 2, L / 2); scene.add(water);
    }
    for (const p of data.sensors.filter((x) => x.elevationM != null && x.offsetM != null)) {
      const m = new THREE.Mesh(new THREE.SphereGeometry(Math.max(1.2, g.h / 40), 16, 12), new THREE.MeshStandardMaterial({ color: LEVEL[p.level] ?? 0x1f6fd1, emissive: LEVEL[p.level] ?? 0x1f6fd1, emissiveIntensity: 0.4 }));
      m.position.set(p.offsetM!, p.elevationM! - g.fl, p.chainageM ?? L / 2); scene.add(m);
    }
    const controls = new OrbitControls(cam, renderer.domElement); controls.target.set(g.b / 2, g.h / 3, L / 2); controls.update();
    let raf = 0; const loop = () => { raf = requestAnimationFrame(loop); controls.update(); renderer.render(scene, cam); }; loop();
    return () => { cancelAnimationFrame(raf); controls.dispose(); renderer.dispose(); el.removeChild(renderer.domElement); };
  }, [data]);
  return <><ErrorBox error={error || fail} /><p className="muted small" style={{ marginTop: 0 }}>{data?.source}. Drag to rotate, scroll to zoom. Sensors without chainage are drawn at mid-length.</p>
    <div ref={ref} style={{ width: '100%', minHeight: 460, borderRadius: 8, overflow: 'hidden', border: '1px solid var(--line)' }} aria-label="3-D model of the dam" /></>;
}
