/** Offline store for field inspections (IndexedDB): downloaded packages, queued operations and photos. */
export interface QueuedOp { clientOpId: string; kind: 'start' | 'checks' | 'finding' | 'complete'; inspectionId: string; payload: unknown; createdAt: string; label: string }
export interface QueuedPhoto { clientRef: string; findingRef: string; inspectionId: string; blob: Blob; fileName: string; latitude?: number; longitude?: number; takenAt: string; caption?: string }
export interface SyncIssue { at: string; what: string; error: string }

const DB = 'dammaster-field'; const STORES = ['packages', 'queue', 'photos', 'issues'] as const;
let dbp: Promise<IDBDatabase> | null = null;
function db(): Promise<IDBDatabase> {
  if (!dbp) dbp = new Promise((res, rej) => {
    const r = indexedDB.open(DB, 1);
    r.onupgradeneeded = () => { const d = r.result; d.createObjectStore('packages'); d.createObjectStore('queue', { keyPath: 'clientOpId' }); d.createObjectStore('photos', { keyPath: 'clientRef' }); d.createObjectStore('issues', { autoIncrement: true }); };
    r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error);
  });
  return dbp;
}
async function tx<T>(store: (typeof STORES)[number], mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const d = await db(); return new Promise((res, rej) => { const t = d.transaction(store, mode); const r = fn(t.objectStore(store)); r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error); });
}
export const putPackage = (id: string, pkg: unknown) => tx('packages', 'readwrite', (s) => s.put(pkg, id));
export const getPackage = <T,>(id: string) => tx<T | undefined>('packages', 'readonly', (s) => s.get(id) as IDBRequest<T | undefined>);
export const allPackages = <T,>() => tx<T[]>('packages', 'readonly', (s) => s.getAll() as IDBRequest<T[]>);
export const deletePackage = (id: string) => tx('packages', 'readwrite', (s) => s.delete(id));
export const queueOp = (op: QueuedOp) => tx('queue', 'readwrite', (s) => s.put(op));
export const queuedOps = async () => (await tx<QueuedOp[]>('queue', 'readonly', (s) => s.getAll() as IDBRequest<QueuedOp[]>)).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
export const queuePhoto = (p: QueuedPhoto) => tx('photos', 'readwrite', (s) => s.put(p));
export const queuedPhotos = () => tx<QueuedPhoto[]>('photos', 'readonly', (s) => s.getAll() as IDBRequest<QueuedPhoto[]>);
export const issues = () => tx<SyncIssue[]>('issues', 'readonly', (s) => s.getAll() as IDBRequest<SyncIssue[]>);
export const clearIssues = () => tx('issues', 'readwrite', (s) => s.clear());
export const newId = () => `${Date.now().toString(36)}-${crypto.getRandomValues(new Uint32Array(2)).join('')}`.slice(0, 60);

/** Shrinks a camera photo to at most 1600 px on the long side (JPEG 80 %) — a 4 MB phone photo becomes ~300 KB. */
export async function shrinkPhoto(file: File): Promise<Blob> {
  const bmp = await createImageBitmap(file); const k = Math.min(1, 1600 / Math.max(bmp.width, bmp.height));
  const c = document.createElement('canvas'); c.width = Math.round(bmp.width * k); c.height = Math.round(bmp.height * k);
  c.getContext('2d')!.drawImage(bmp, 0, 0, c.width, c.height);
  return new Promise((res) => c.toBlob((b) => res(b!), 'image/jpeg', 0.8));
}
export const position = (): Promise<GeolocationPosition | null> => new Promise((res) => { if (!navigator.geolocation) return res(null); navigator.geolocation.getCurrentPosition(res, () => res(null), { timeout: 5000, maximumAge: 60000 }); });

/** Replays queued operations in order, then uploads photos. Every operation and photo carries its own id, so a retry never duplicates. */
let inFlight: Promise<{ applied: number; rejected: number; photos: number; pending: number }> | null = null;
/** One sync at a time: concurrent callers (button, 'online' event) share the running sync. */
export function sync() { if (!inFlight) inFlight = doSync().finally(() => { inFlight = null; }); return inFlight; }
async function doSync(): Promise<{ applied: number; rejected: number; photos: number; pending: number }> {
  const ops = await queuedOps(); let applied = 0, rejected = 0, photos = 0;
  if (ops.length) {
    const r = await fetch('/api/field/sync', { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'DamMaster' }, body: JSON.stringify({ ops: ops.map(({ clientOpId, kind, inspectionId, payload }) => ({ clientOpId, kind, inspectionId, payload })) }) });
    if (!r.ok) throw new Error(r.status === 401 ? 'Signed out — sign in again, then sync.' : `Sync failed (HTTP ${r.status}).`);
    const res: { clientOpId: string; ok: boolean; error?: string }[] = await r.json();
    for (const x of res) {
      const op = ops.find((o) => o.clientOpId === x.clientOpId);
      if (x.ok) applied++; else { rejected++; await tx('issues', 'readwrite', (s) => s.add({ at: new Date().toISOString(), what: op?.label ?? x.clientOpId, error: x.error ?? 'rejected' })); }
      await tx('queue', 'readwrite', (s) => s.delete(x.clientOpId));
    }
  }
  for (const p of await queuedPhotos()) {
    const fd = new FormData(); fd.append('file', p.blob, p.fileName); fd.append('clientRef', p.clientRef); fd.append('takenAt', p.takenAt);
    if (p.latitude != null) { fd.append('latitude', String(p.latitude)); fd.append('longitude', String(p.longitude)); } if (p.caption) fd.append('caption', p.caption);
    const r = await fetch(`/api/findings/${p.findingRef}/photos`, { method: 'POST', body: fd, credentials: 'include', headers: { 'X-Requested-With': 'DamMaster' } });
    if (r.ok) { photos++; await tx('photos', 'readwrite', (s) => s.delete(p.clientRef)); }
    else if (r.status === 400) { const j = await r.json(); if (!String(j.error).includes('sync the finding first')) { await tx('issues', 'readwrite', (s) => s.add({ at: new Date().toISOString(), what: `Photo ${p.fileName}`, error: j.error })); await tx('photos', 'readwrite', (s) => s.delete(p.clientRef)); } }
  }
  return { applied, rejected, photos, pending: (await queuedOps()).length + (await queuedPhotos()).length };
}
