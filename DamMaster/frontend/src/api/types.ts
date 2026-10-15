// Shapes returned by the ASP.NET Core API (camelCase JSON). Keep in sync with backend Models.cs.
export type Role = 'Admin' | 'DamSafetyEngineer' | 'InstrumentationEngineer' | 'Inspector' | 'Approver' | 'Management' | 'DesignEngineer' | 'Checker';
export interface User { id: string; username: string; fullName: string; role: Role; isActive: boolean; mustChangePassword: boolean; lastLoginAt?: string | null; email?: string | null; phone?: string | null; offline?: boolean }
export interface Dam {
  id: string; code: string; name: string; state?: string | null; district?: string | null; river?: string | null;
  latitude?: number | null; longitude?: number | null; damTypeCode: string; functionCode?: string | null; hazardCode: string;
  heightM?: number | null; crestLengthM?: number | null; frlM?: number | null; mwlM?: number | null; mddlM?: number | null; crestLevelM?: number | null;
  grossStorageMcm?: number | null; seismicZone?: string | null; isSpecified: boolean; lifePhase: string; eapStatus?: string | null; ownerName?: string | null;
}
export interface DamSummary {
  id: string; code: string; name: string; state?: string; damTypeCode: string; hazardCode: string; lifePhase: string;
  sensors: number; readings: number; dataAsOf?: string | null; openAlerts: number; maxOpenLevel: number; l1: number; l2: number; l3: number;
  reservoirLevel?: number | null; frlM?: number | null; activePfma: number; dhi?: number | null; dhiBand?: string | null; risk?: number | null; rating?: string | null;
}
export interface SensorStatus {
  id: string; code: string; parameterCode: string; parameterName: string; location?: string; engUnit: string; status: string;
  latestValue?: number | null; latestAt?: string | null; latestQuality?: string | null; readingCount: number; openAlerts: number; maxOpenLevel: number;
  alertValue?: number | null; alarmValue?: number | null; emergencyValue?: number | null; thresholdApproved: boolean;
}
export interface Sensor {
  id: string; damId: string; code: string; parameterCode: string; instrumentTypeCode?: string | null; location?: string | null; elevationM?: number | null;
  engUnit: string; expectedMin?: number | null; expectedMax?: number | null; direction: 'Rising' | 'Falling'; readingIntervalMin: number; calibrationDue?: string | null; status: string;
  latitude?: number | null; longitude?: number | null; offsetM?: number | null; chainageM?: number | null;
}
export interface Threshold {
  id: string; sensorId: string; alertValue?: number | null; alarmValue?: number | null; emergencyValue?: number | null; rateLimitPerDay?: number | null;
  zLimit: number; persistence: number; method: string; basis?: string | null; createdBy?: string; createdAt: string; approvedBy?: string | null; approvedAt?: string | null; isCurrent: boolean;
}
export interface ReadingPoint { t: string; v: number; q: string; rule?: string | null }
export interface Pfma { id: string; damId: string; failureModeCode: string; location?: string; description?: string; likelihood?: string; consequence?: string; status: string; createdBy?: string; createdAt: string; sensorIds: string[] }
export interface Alert {
  id: string; damId: string; damName?: string; sensorId?: string | null; sensorCode?: string | null; pfmaId?: string | null; parameterCode?: string | null;
  level: number; ruleType: string; title: string; detail?: string; value?: number | null; limitValue?: number | null; firstAt: string; lastAt: string; occurrences: number;
  status: 'Open' | 'Acknowledged' | 'Closed'; ackDueAt?: string | null; escalationStep: number; inspectionId?: string | null; ackBy?: string; ackAt?: string; ackNote?: string; closedBy?: string; closedAt?: string; disposition?: string; closeNote?: string;
}
export interface ImportIssue { sheet: string; rowNo: number; itemCode?: string; outcome: string; ruleCode: string; message: string }
export interface ImportResult {
  importId?: string; committed: boolean; rowsRead: number; good: number; suspect: number; bad: number; rejected: number; quarantined: number; duplicates: number;
  damsUpserted: number; sensorsUpserted: number; issues: ImportIssue[]; alertsRaised: Alert[];
}
export interface RefItem { code: string; name: string; [k: string]: unknown }
export interface Standard { code: string; title: string; subject?: string; category: string; edition?: string | null; clauses?: string | null; verifiedBy?: string | null; verifiedAt?: string | null }
export interface Interpretation { id: number; groupCode: string; observation: string; normalPattern?: string; warningSign?: string; criticalSign?: string; possibleCause?: string; actionText?: string }
export interface AuditEntry { id: number; at: string; username?: string; module: string; action: string; entity?: string; entityId?: string }

// ---------- Phase 2 ----------
export interface Inspection { id: string; damId: string; damName?: string; inspectionType: string; scheduledDate: string; status: 'Planned' | 'In progress' | 'Completed' | 'Cancelled'; inspector?: string | null; startedAt?: string | null; completedAt?: string | null; overallCondition?: string | null; summary?: string | null; triggerAlertId?: string | null; createdBy?: string; createdAt: string; openFindings: number; checksDone: number }
export interface InspectionItem { code: string; materialGroup: string; component: string; item: string; result?: string | null; remark?: string | null; checkedBy?: string | null }
export interface Finding { id: string; inspectionId: string; damId: string; damName?: string; inspectionType?: string; component: string; location?: string | null; description: string; severity: 'Low' | 'Medium' | 'High' | 'Critical'; sensorId?: string | null; sensorCode?: string | null; recommendation?: string | null; status: string; dueDate?: string | null; alertId?: string | null; createdBy?: string; createdAt: string; closedBy?: string | null; closedAt?: string | null; closureNote?: string | null }
export interface EapContact { id: string; damId: string; name: string; organisation?: string | null; eapRole?: string | null; phone?: string | null; email?: string | null; minLevel: number; escalationOrder: number; isActive: boolean }
export interface NotificationRow { id: number; alertId?: string | null; damId?: string | null; level: number; channel: 'InApp' | 'Email' | 'SMS'; recipient: string; recipientName?: string | null; subject: string; body: string; escalationStep: number; status: string; attempts: number; error?: string | null; createdAt: string; sentAt?: string | null; readAt?: string | null }
export interface ScheduleRow { sensorId: string; code: string; parameterCode: string; groupCode: string; requiredHours?: number | null; note?: string | null; configuredMinutes: number; lastReading?: string | null; nextDue?: string | null; status: string; readingsLast30d: number; compliancePct?: number | null; loggerSlowerThanRequired: boolean }
export interface MonsoonCompliance { damId: string; damName: string; year: number; preStatus: string; preDue: string; preDone?: string | null; postStatus: string; postDue: string; postDone?: string | null; openFindings: number; overdueFindings: number }

// ---------- Phase 3 ----------
export interface HstModel { id: string; sensorId: string; variant: string; dataFrom: string; dataTo: string; nPoints: number; hMin: number; hMax: number; t0: string; r2: number; sigma: number; kLimit: number; driftPerYear?: number | null; status: 'Draft' | 'Active' | 'Superseded' | 'Rejected'; fittedBy?: string; fittedAt: string; approvedBy?: string | null; approvedAt?: string | null; notes?: string | null }
export interface HstPoint { t: string; measured: number; predicted: number; hydro: number; season: number; time: number; residual: number; rwl: number }
export interface HstResponse { models: HstModel[]; shown: HstModel | null; coefficients: Record<string, number> | null; series: HstPoint[] | null }
export interface LagR { lagHours: number; n: number; r: number | null }
export interface ResponseResult {
  sensor: string; unit: string;
  reservoir: { lags: LagR[]; bestLagHours?: number; bestR?: number; slopePerMetre: number; scatter: { h: number; v: number }[]; interpretation: string } | null;
  rain: { gauge: string; eventThresholdMm: number; lags: LagR[]; bestLagHours?: number; bestR?: number; events: { day: string; mm: number; rise: number | null }[]; interpretation: string } | null;
}
export interface DhiComponent { code: string; name: string; weight: number; score: number; reasons: string[] }
export interface DhiResult { damId: string; damName: string; computedAt: string; dataAsOf?: string | null; dhi: number; band: string; risk: number; rating: string; components: DhiComponent[]; riskReasons: string[] }
export interface DhiSnapshot { id: number; computedAt: string; dhi: number; band: string; risk: number; rating: string }
export interface DamDocument { id: string; damId: string; docType: string; title: string; referenceNo?: string | null; issueDate?: string | null; nextReviewDate?: string | null; status: string; createdBy?: string; createdAt?: string }
export interface AvailabilityRow { sensorId: string; code: string; parameterCode: string; intervalMin: number; expected: number; received: number; valid: number; suspect: number; bad: number; availabilityPct: number; validPct: number; longestGapHours: number; lastReading?: string | null }
export interface AvailabilityReport { damId: string; damName: string; windowStart: string; windowEnd: string; days: number; relativeTo: string; availabilityPct: number; validPct: number; rows: AvailabilityRow[] }

// ---------- Phase 4 ----------
export interface GatewayHealth { id: string; code: string; damId: string; damCode?: string; damName?: string; isActive: boolean; expectedIntervalMin: number; transport?: string | null; lastSeenAt?: string | null; lastMessageAt?: string | null; lastStatusAt?: string | null; batteryV?: number | null; signalDbm?: number | null; bufferedCount?: number | null; firmware?: string | null; messages24h: number; readings24h: number; rejected24h: number; late24h: number; avgLatencyS?: number | null; openAlerts: number; state: string; createdAt: string }
export interface GatewayMessageRow { id: number; messageId: string; transport: string; receivedAt: string; readings: number; accepted: number; rejected: number; quarantined: number; duplicates: number; late: number; maxLatencyS?: number | null; error?: string | null }
export interface GatewayStatusRow { at: string; batteryV?: number | null; signalDbm?: number | null; bufferedCount?: number | null; uptimeS?: number | null; firmware?: string | null }
export interface ReconciliationRow { sensorId: string; code: string; intervalMin: number; expected: number; received: number; backfilled: number; gaps: number; longestGapHours: number; topGaps: { from: string; to: string; hours: number }[] }
export interface MqttTopics { telemetryTopic: string; statusTopic: string; ackTopic: string }
export interface SystemStatus { version: string; mqtt: { enabled: boolean; connected: boolean; broker?: string | null; topics: string[]; connectedSince?: string | null; lastMessageAt?: string | null; received: number; failed: number; duplicateMessages: number; lastError?: string | null }; jobs: { job: string; lastRunAt: string; lastOk: boolean; lastResult?: string | null }[]; storage: { readings: number; dailyAggregates: number; partitioned: boolean; partitions: { name: string; rows: number }[]; databaseSizeMb?: number | null }; serverTimeUtc: string }

// ---------- Phase 5 ----------
export interface DesignInputDef { key: string; label: string; unit?: string | null; group: string; kind: 'number' | 'select' | 'text'; default: number | string; options?: string[] | null; help?: string | null }
export interface DesignCatalog { inputs: DesignInputDef[]; calcs: { code: string; title: string; dependsOn: string[] }[]; defaultEa: number[][] }
export interface DesignProjectRow { id: string; code: string; name: string; damId?: string | null; damName?: string | null; status: string; createdBy?: string; createdAt: string; updatedAt?: string | null; staleCount: number; approvedCount: number; calcCount: number }
export interface DesignResult { label: string; value: string; unit: string }
export interface DesignCheck { label: string; value: string; limit: string; ok: boolean; unit: string; max: boolean }
export interface DesignCodeRef { code: string; title: string; verified: boolean }
export interface DesignCalc { code: string; title: string; version: number; status: 'Draft' | 'Checked' | 'Approved'; stale: boolean; staleReason?: string | null; runBy: string; runAt: string; checkedBy?: string | null; checkedAt?: string | null; approvedBy?: string | null; approvedAt?: string | null; maturity: string; results: DesignResult[]; checks: DesignCheck[]; interpretation: string; data: any; codes: DesignCodeRef[] }
export interface DesignProjectDetail { project: DesignProjectRow; inputs: Record<string, number | string>; ea: number[][]; calcs: DesignCalc[] }
export interface DesignRecord { code: string; title: string; inputs: Record<string, string>; assumptions: string[]; codes: string[]; method: string; formula: string; steps: string[]; results: DesignResult[]; checks: DesignCheck[]; interpretation: string }

// ---------- Phase 6 ----------
export interface SurveyDataset { id: string; damId: string; damName?: string; kind: 'InSAR' | 'Survey'; name: string; provider?: string | null; epochDate?: string | null; periodFrom?: string | null; periodTo?: string | null; unit: string; threshold?: number | null; pointCount: number; notes?: string | null; createdBy?: string; createdAt: string }
export interface DocFile { id: string; documentId: string; fileName: string; sizeBytes: number; pages?: number | null; textChars: number; extractedOk: boolean; extractNote?: string | null; createdBy?: string; createdAt: string }
export interface SearchHit { id: number; fileId: string; documentId: string; damId: string; page?: number | null; text: string; score: number; title?: string; docType?: string; damName?: string; fileName?: string }
export interface AskResult { answer: string | null; configured: boolean; note: string; sources: { n: number; title: string; docType?: string; damName?: string; page?: number | null; fileId: string; fileName?: string; text: string }[] }
export interface TwinSensor { id: string; code: string; parameterCode: string; unit: string; location?: string; elevationM?: number | null; offsetM?: number | null; chainageM?: number | null; latest?: number | null; latestAt?: string | null; level: number }
export interface Twin { dam: { id: string; name: string; frlM?: number | null; mwlM?: number | null; mddlM?: number | null }; geometry: { type: string; h: number; b: number; t: number; crest: number; fl: number; us: number; ds: number; kind: string }; source: string; reservoirLevel?: number | null; tailwaterLevel?: number | null; sensors: TwinSensor[]; unplaced: number }
export interface ThresholdSuggestion { sensorId: string; code: string; parameterCode: string; unit: string; alert?: number | null; alarm?: number | null; emergency?: number | null; ok: boolean; basis: string }
