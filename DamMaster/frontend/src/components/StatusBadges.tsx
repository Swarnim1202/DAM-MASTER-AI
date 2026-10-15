export const SEVERITY_CLASS: Record<string, string> = { Low: 'b-info', Medium: 'b-1', High: 'b-2', Critical: 'b-3' };
export const SeverityBadge = ({ s }: { s: string }) => <span className={`badge ${SEVERITY_CLASS[s] ?? 'b-mute'}`}>{s}</span>;
const INSP: Record<string, string> = { Planned: 'b-info', 'In progress': 'b-1', Completed: 'b-0', Cancelled: 'b-mute' };
export const InspectionBadge = ({ s }: { s: string }) => <span className={`badge ${INSP[s] ?? 'b-mute'}`}>{s}</span>;
const COMP: Record<string, string> = { 'Done on time': 'b-0', 'Done late': 'b-1', Due: 'b-info', Overdue: 'b-3' };
export const ComplianceBadge = ({ s }: { s: string }) => <span className={`badge ${COMP[s] ?? 'b-mute'}`}>{s}</span>;
const SCH: Record<string, string> = { 'On time': 'b-0', Due: 'b-1', Overdue: 'b-3', 'No data': 'b-mute', 'Event-based': 'b-info' };
export const ScheduleBadge = ({ s }: { s: string }) => <span className={`badge ${SCH[s] ?? 'b-mute'}`}>{s}</span>;
