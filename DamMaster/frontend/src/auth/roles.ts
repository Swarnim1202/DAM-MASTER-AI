import type { Role } from '../api/types';

// Mirror of backend Policies.Map — the server enforces; the UI only hides or locks controls.
export const ROLE_LABEL: Record<Role, string> = {
  Admin: 'System Administrator', DamSafetyEngineer: 'Dam Safety Engineer', InstrumentationEngineer: 'Instrumentation Engineer',
  Inspector: 'Inspector', Approver: 'Approver', Management: 'Management (read-only)', DesignEngineer: 'Design Engineer', Checker: 'Checker / Reviewer',
};
export const POLICY = {
  manageDams: ['Admin', 'DamSafetyEngineer'],
  manageSensors: ['Admin', 'InstrumentationEngineer', 'DamSafetyEngineer'],
  importData: ['Admin', 'InstrumentationEngineer'],
  approveThresholds: ['DamSafetyEngineer', 'Approver'],
  acknowledgeAlerts: ['DamSafetyEngineer', 'InstrumentationEngineer'],
  closeAlerts: ['DamSafetyEngineer'],
  managePfma: ['DamSafetyEngineer'],
  verifyStandards: ['DamSafetyEngineer', 'Approver'],
  manageUsers: ['Admin'],
  manageGateways: ['Admin', 'InstrumentationEngineer'],
  evaluate: ['Admin', 'DamSafetyEngineer', 'InstrumentationEngineer'],
  viewAudit: ['Admin', 'Approver', 'DamSafetyEngineer', 'Management'],
  manageInspections: ['Inspector', 'DamSafetyEngineer'],
  closeFindings: ['DamSafetyEngineer'],
  manageEap: ['DamSafetyEngineer', 'Admin'],
  viewNotificationLog: ['Admin', 'DamSafetyEngineer'],
  runNotifications: ['Admin'],
  fitModels: ['DamSafetyEngineer', 'InstrumentationEngineer'],
  manageDocuments: ['DamSafetyEngineer', 'Admin'],
  dhiSnapshot: ['Admin', 'DamSafetyEngineer'],
  viewSystem: ['Admin', 'DamSafetyEngineer', 'InstrumentationEngineer'],
  runJobs: ['Admin'],
  editDesign: ['DesignEngineer', 'Admin'],
  checkDesign: ['Checker', 'DamSafetyEngineer'],
  approveDesign: ['Approver'],
  manageSurveys: ['DamSafetyEngineer', 'InstrumentationEngineer', 'Admin'],
  applyDesignThresholds: ['DamSafetyEngineer'],
  viewPortfolio: ['Admin', 'Management', 'Approver', 'DamSafetyEngineer'],
  fieldInspect: ['Inspector', 'DamSafetyEngineer'],
} as const satisfies Record<string, readonly Role[]>;
export type Policy = keyof typeof POLICY;
export const allowed = (role: Role | undefined, p: Policy) => !!role && (POLICY[p] as readonly Role[]).includes(role);
