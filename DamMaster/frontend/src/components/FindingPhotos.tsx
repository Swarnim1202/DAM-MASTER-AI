import { useState } from 'react';
import { useApi } from '../api/hooks';
import { fmtTime } from '../api/client';
import { Modal } from './ui';

interface Photo { id: string; fileName: string; width?: number | null; height?: number | null; latitude?: number | null; longitude?: number | null; takenAt?: string | null; caption?: string | null; createdBy?: string }

/** Thumbnails of a finding's photos with a full-size viewer (location and time shown when the device recorded them). */
export default function FindingPhotos({ findingId }: { findingId: string }) {
  const { data } = useApi<Photo[]>(`/findings/${findingId}/photos`); const [open, setOpen] = useState<Photo | null>(null);
  if (!data?.length) return null;
  return <div className="row" style={{ marginTop: 4, gap: 4 }}>{data.map((p) => <img key={p.id} src={`/api/photos/${p.id}`} alt={p.caption ?? 'Finding photo'} loading="lazy" onClick={() => setOpen(p)}
    style={{ width: 56, height: 42, objectFit: 'cover', borderRadius: 4, border: '1px solid var(--line)', cursor: 'zoom-in' }} />)}
    {open && <Modal title={open.caption ?? open.fileName} onClose={() => setOpen(null)} wide><img src={`/api/photos/${open.id}`} alt={open.caption ?? 'Finding photo'} style={{ width: '100%', borderRadius: 6 }} />
      <p className="muted small">{open.width}×{open.height} · taken {fmtTime(open.takenAt)} by {open.createdBy}{open.latitude != null && <> · <a href={`https://www.openstreetmap.org/?mlat=${open.latitude}&mlon=${open.longitude}#map=18/${open.latitude}/${open.longitude}`} target="_blank" rel="noreferrer">{Number(open.latitude).toFixed(5)}, {Number(open.longitude).toFixed(5)}</a></>}</p></Modal>}</div>;
}
