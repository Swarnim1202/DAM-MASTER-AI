import { fmtTime } from '../api/client';
import { useApi } from '../api/hooks';
import type { AuditEntry } from '../api/types';
import { ErrorBox } from '../components/ui';

export default function Audit() {
  const { data, error } = useApi<AuditEntry[]>('/audit?take=500');
  return (
    <>
      <div className="hero"><div><h1>Audit trail</h1><p>Every sign-in, import, threshold change, approval, alert action and verification, with the person who did it.</p></div></div>
      <ErrorBox error={error} />
      <div className="card"><div className="tablewrap" style={{ maxHeight: '70vh' }}><table><thead><tr><th>Time (IST)</th><th>User</th><th>Module</th><th>Action</th><th>Entity</th></tr></thead>
        <tbody>{data?.map((a) => <tr key={a.id}><td>{fmtTime(a.at)}</td><td>{a.username}</td><td>{a.module}</td><td>{a.action}</td><td className="muted small">{a.entity}</td></tr>)}</tbody></table></div></div>
    </>
  );
}
