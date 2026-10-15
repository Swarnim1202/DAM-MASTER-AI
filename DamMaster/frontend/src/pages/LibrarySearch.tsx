import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { useApi } from '../api/hooks';
import type { AskResult, Dam, SearchHit } from '../api/types';
import { ErrorBox } from '../components/ui';

const mark = (text: string, q: string) => { const words = q.toLowerCase().split(/\W+/).filter((w) => w.length > 3); return text.split(/(\s+)/).map((t, i) => words.some((w) => t.toLowerCase().startsWith(w)) ? <mark key={i}>{t}</mark> : t); };

export default function LibrarySearch() {
  const dams = useApi<Dam[]>('/dams'); const [q, setQ] = useState(''); const [damId, setDamId] = useState(''); const [hits, setHits] = useState<SearchHit[] | null>(null);
  const [ask, setAsk] = useState<AskResult | null>(null); const [err, setErr] = useState<string | null>(null); const [busy, setBusy] = useState(false);
  const search = async (e?: FormEvent) => { e?.preventDefault(); setBusy(true); setAsk(null); try { setHits(await api.get<SearchHit[]>(`/library/search?q=${encodeURIComponent(q)}${damId ? '&damId=' + damId : ''}`)); setErr(null); } catch (x) { setErr((x as Error).message); } finally { setBusy(false); } };
  const doAsk = async () => { setBusy(true); try { setAsk(await api.post<AskResult>('/library/ask', { question: q, damId: damId || null })); setErr(null); } catch (x) { setErr((x as Error).message); } finally { setBusy(false); } };
  return (
    <>
      <div className="hero"><div><h1>Document search</h1><p>Full-text search across the uploaded files of every registered document (EAP, O&amp;M manuals, safety reviews, design reports). "Ask" sends the best passages to the configured AI model, which must answer only from them and cite them.</p></div></div>
      <form className="card row" onSubmit={search}><input className="input" style={{ flex: 1, minWidth: 260 }} value={q} onChange={(e) => setQ(e.target.value)} placeholder="e.g. turbid seepage emergency action, uplift drain clogging, gate operation test" aria-label="Search text" />
        <select className="input" style={{ width: 'auto' }} value={damId} onChange={(e) => setDamId(e.target.value)} aria-label="Dam"><option value="">All dams</option>{dams.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}</select>
        <button className="btn primary" disabled={busy || q.trim().length < 3}>Search</button><button type="button" className="btn" disabled={busy || q.trim().length < 3} onClick={doAsk}>✦ Ask</button></form>
      <ErrorBox error={err} />
      {ask && <div className="card mt"><h2>Answer</h2>{ask.answer ? <p>{ask.answer}</p> : null}<p className="note">{ask.note}</p>
        <ol className="small">{ask.sources.map((s) => <li key={s.n}><b>{s.title}</b>{s.page ? `, page ${s.page}` : ''} · {s.damName} — <a href={`/api/files/${s.fileId}`}>{s.fileName}</a><div className="muted">{s.text.slice(0, 300)}…</div></li>)}</ol></div>}
      {hits && <div className="card mt"><h2>{hits.length} passage(s)</h2>{hits.map((h) => <div key={h.id} style={{ borderTop: '1px solid var(--line)', padding: '10px 0' }}>
        <div className="row"><b>{h.title}</b><span className="badge b-mute">{h.docType}</span><span className="muted small">{h.damName}{h.page ? ` · page ${h.page}` : ''} · score {h.score.toFixed(2)}</span><a className="small" href={`/api/files/${h.fileId}`}>{h.fileName}</a></div>
        <p className="small" style={{ margin: '4px 0 0' }}>{mark(h.text.slice(0, 600), q)}{h.text.length > 600 ? '…' : ''}</p></div>)}{hits.length === 0 && <p className="muted">No passage matches.</p>}</div>}
    </>
  );
}
