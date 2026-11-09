// Workflow feature types — mirrors backend DTOs for definitions, states, transitions, configs

export interface WorkflowStateDto {
  id: string;
  name: string;
  displayNameVi: string;
  displayNameEn: string;
  isInitial: boolean;
  isFinal: boolean;
  color: string;
  sortOrder: number;
  slaHours?: number | null;
  autoTransitionOnTimeoutId?: string | null;
  /** Visual designer canvas X coordinate (null = let FE auto-layout). */
  positionX?: number | null;
  /** Visual designer canvas Y coordinate (null = let FE auto-layout). */
  positionY?: number | null;
}

export interface WorkflowTransitionDto {
  id: string;
  fromStateId: string;
  toStateId: string;
  actionName: string;
  actionLabelVi: string;
  actionLabelEn: string;
  conditionsJson?: string | null;
  sortOrder: number;
  isRecall: boolean;
  /** UUID of the default approver for parallel-branch child steps on this transition */
  defaultAssigneeId?: string | null;
  /** Source handle anchor on origin node (vd "r-out"). Null = ReactFlow default. */
  sourceHandle?: string | null;
  /** Target handle anchor on destination node (vd "l-in"). Null = ReactFlow default. */
  targetHandle?: string | null;
  /** Per-transition handler-assignment payload (plan 260523-1241). Null = no rule (Q2 replace-all DELETE on Save). */
  assignment?: AssignmentInput | null;
}

/**
 * Principal-resolution mode of one WorkflowTransitionAssignmentClause (plan 260523-1241 Q6).
 * String union matching the wire contract: the API serialises enums as their C# member name
 * (JsonStringEnumConverter), so a clause round-trips as e.g. "Initiator" on GET. All modes are
 * save-supported (RT2-15 lifted the earlier NotSupported set).
 */
export type AssignmentClauseMode =
  | 'SpecificUser'
  | 'Initiator'
  | 'PreviousHandler'
  | 'ByPermission'
  | 'ByRole'
  | 'ByGroup'
  | 'ByOrgUnit';

/**
 * How multiple clauses combine — "Union" (default), "Intersect" (NotSupported at MVP per RT2-15).
 */
export type AssignmentCombineMode = 'Union' | 'Intersect';

/**
 * One resolution clause inside an AssignmentInput.
 * RefsJson:
 *   - null for ref-less modes (Initiator)
 *   - JSON array string for ByPermission (e.g. '["WORKFLOW.TASK.PROCESS"]') — R3 canonicalised at Save
 *   - JSON array string for SpecificUser (e.g. '["uuid-1","uuid-2"]')
 */
export interface AssignmentClauseInput {
  mode: AssignmentClauseMode;
  refsJson: string | null;
}

/**
 * Per-transition assignment payload (plan 260523-1241 Option A).
 * Validator R2-R4 (Phase 03) enforces shape at Save; repo Phase 3 (Phase 04) persists post-validator.
 */
export interface AssignmentInput {
  combineMode: AssignmentCombineMode;
  allowAssignorOverride: boolean;
  clauses: AssignmentClauseInput[];
}

/**
 * Parallel-branch join strategy. API serialises the enum as its C# member name
 * (JsonStringEnumConverter), so a branch round-trips as e.g. "And" on GET; the save payload
 * may send the same string names.
 */
export type BranchType = 'And' | 'Or' | 'NOfM';

/** Possible outcome of a branch child — drives join logic + optional redirect. */
export type BranchResolutionType = 'Approved' | 'Rejected' | 'Escalated' | 'Timeout';

/** GET read-back shape of one branch resolution (id-keyed: resultingStateId is a long state id). */
export interface WorkflowBranchResolutionDto {
  resolutionType: BranchResolutionType;
  resultingStateId?: string | null;
}

/** GET read-back shape of one branch child (id-keyed). */
export interface WorkflowBranchChildDto {
  id: string;
  targetStateId: string;
  name: string;
  displayNameVi: string;
  departmentId?: string | null;
  defaultAssigneeId?: string | null;
  sortOrder: number;
  resolutions: WorkflowBranchResolutionDto[];
  /** Per-child handler-assignment pool (same wire shape as a transition assignment). Null = no pool. */
  assignment?: AssignmentInput | null;
}

/**
 * GET read-back shape of one parallel branch (single draggable fork gateway). State refs are long
 * ids (snapshot-internal-consistent — the canvas keys nodes by state id). positionX/Y restore the
 * dragged fork diamond; null ⇒ the canvas auto-derives an initial position.
 */
export interface WorkflowBranchDto {
  id: string;
  branchingStateId: string;
  continuationStateId: string;
  branchType: BranchType;
  requiredCount?: number | null;
  allowCancelOnAnyReject: boolean;
  timeoutMinutes: number;
  positionX?: number | null;
  positionY?: number | null;
  children: WorkflowBranchChildDto[];
}

export interface WorkflowDefinitionDto {
  id: string;
  name: string;
  description: string;
  entityType: string;
  isActive: boolean;
  tenantId: string;
  createdAt: string;
  definitionKey: string;
  version: number;
  isLatest: boolean;
  states: WorkflowStateDto[];
  transitions: WorkflowTransitionDto[];
  /** Parallel branches authored on the visual designer (fork → children → continuation join). */
  branches?: WorkflowBranchDto[];
}

export interface WorkflowDefinitionListDto {
  id: string;
  name: string;
  description?: string;
  entityType: string;
  isActive: boolean;
  definitionKey: string;
  version: number;
  isLatest: boolean;
  stateCount: number;
  transitionCount: number;
  createdAt: string;
}

export interface CreateWorkflowRequest {
  name: string;
  description?: string;
  entityType: string;
  states: GraphStateInput[];
  transitions: GraphTransitionInput[];
}

export interface GraphStateInput {
  name: string;
  displayNameVi: string;
  displayNameEn: string;
  isInitial: boolean;
  isFinal: boolean;
  color: string;
  sortOrder: number;
  slaHours?: number | null;
  autoTransitionOnTimeoutId?: string | null;
  positionX?: number | null;
  positionY?: number | null;
}

export interface GraphTransitionInput {
  fromStateName: string;
  toStateName: string;
  actionName: string;
  actionLabelVi: string;
  actionLabelEn: string;
  conditionsJson?: string | null;
  sortOrder: number;
  isRecall?: boolean;
  /** UUID of the default approver for parallel-branch child steps on this transition */
  defaultAssigneeId?: string | null;
  sourceHandle?: string | null;
  targetHandle?: string | null;
  /** Per-transition handler-assignment payload (plan 260523-1241). Null = no rule (Q2 replace-all DELETE on Save). */
  assignment?: AssignmentInput | null;
}

/** Save-shape (name-keyed) branch resolution — resultingStateName resolved BE-side to a long id. */
export interface GraphBranchResolutionInput {
  resolutionType: BranchResolutionType;
  resultingStateName?: string | null;
}

/** Save-shape (name-keyed) branch child. */
export interface GraphBranchChildInput {
  targetStateName: string;
  name: string;
  displayNameVi: string;
  sortOrder: number;
  resolutions: GraphBranchResolutionInput[];
  departmentId?: string | null;
  defaultAssigneeId?: string | null;
  /** Per-child handler-assignment pool (reuses the transition AssignmentInput shape). Null = no pool. */
  assignment?: AssignmentInput | null;
}

/**
 * Save-shape (name-keyed) parallel branch. Mirrors the transition asymmetry: state references go
 * out by NAME (replace-all regenerates state ids each save). positionX/Y carry the fork drag spot.
 */
export interface GraphBranchInput {
  branchingStateName: string;
  continuationStateName: string;
  branchType: BranchType;
  allowCancelOnAnyReject: boolean;
  timeoutMinutes: number;
  children: GraphBranchChildInput[];
  requiredCount?: number | null;
  positionX?: number | null;
  positionY?: number | null;
}

export interface SaveGraphRequest {
  states: GraphStateInput[];
  transitions: GraphTransitionInput[];
  /** Parallel branches — omitted/empty ⇒ BE clears all branch rows (replace-all). */
  branches?: GraphBranchInput[];
}

export interface WorkflowNotificationConfigDto {
  id: string;
  definitionId: string;
  actionName: string;
  channel: string;
  recipientType: string;
  recipientValue: string;
  subject: string;
  bodyTemplate: string;
  isActive: boolean;
}

export interface CreateNotificationConfigRequest {
  actionName: string;
  channel: string;
  recipientType: string;
  recipientValue: string;
  subject: string;
  bodyTemplate: string;
}

export interface WorkflowAssignmentRuleDto {
  id: string;
  tenantId: string;
  workflowDefinitionId: string;
  entityType?: string | null;
  entitySubType?: string | null;
  specificity: number;
  priority: number;
  isActive: boolean;
  description?: string | null;
  effectiveFrom: string;
  effectiveUntil?: string | null;
}

export interface CreateAssignmentRuleRequest {
  workflowDefinitionId: string;
  entityType?: string | null;
  entitySubType?: string | null;
  priority: number;
  description?: string | null;
  effectiveFrom: string;
  effectiveUntil?: string | null;
}

// ─── Task & Instance types ─────────────────────────────────────────────────────

export interface WorkflowTaskDto {
  id: string;
  workflowInstanceId: string;
  title: string;
  description?: string | null;
  assigneeId?: string | null;
  status: number; // 0=Open, 1=InProgress, 2=Done, 3=Cancelled
  priority: number; // 0=Low, 1=Normal, 2=High, 3=Urgent
  dueDate?: string | null;
  completedAt?: string | null;
  completedBy?: string | null;
  tenantId: string;
  createdAt: string;
  createdBy: string;
  /** Set when task was auto-delegated or manually delegated — holds original assignee ID */
  delegatedFromUserId?: string | null;
}

/** One candidate principal of a pool (unassigned) task. principalType: 0=Permission,1=Role,2=Group,3=OrgUnit. */
export interface WorkflowTaskCandidateDto {
  principalType: number;
  /** Canonical ref — role NormalizedName (UPPER), or group/org-unit/permission code. Resolved to a label client-side. */
  principalRefId: string;
}

/** A user eligible to claim a pool task (resolved from Role/Group/OrgUnit candidates). */
export interface WorkflowEligibleUserDto {
  userId: string;
  fullName?: string | null;
}

/** Mirrors BE WorkflowInstanceTaskDto — task enriched with assignee name + candidate pool for the detail page. */
export interface WorkflowInstanceTaskDto {
  id: string;
  workflowInstanceId: string;
  title: string;
  status: string; // enum serialized as string: 'Open' | 'InProgress' | 'Done' | 'Cancelled'
  priority: string; // enum serialized as string: 'Low' | 'Normal' | 'High' | 'Urgent'
  dueDate?: string | null;
  assigneeId?: string | null;
  /** Resolved full name of the current assignee; null for pool tasks or unresolved users. */
  assigneeName?: string | null;
  /** Candidate principals when the task is an unclaimed pool task (assigneeId null). */
  candidates: WorkflowTaskCandidateDto[];
  /** Up to ~20 users who can claim this pool task (Role/Group/OrgUnit candidates resolved server-side). */
  eligibleUsers: WorkflowEligibleUserDto[];
  /** Total distinct eligible users — may exceed eligibleUsers.length (capped list). */
  eligibleUserCount: number;
  /** True when a Permission candidate exists whose users are NOT resolved → the list/count is INCOMPLETE. */
  hasUnresolvedPermissionCandidate: boolean;
}

// ─── Delegation types ──────────────────────────────────────────────────────────

/** Mirrors BE DelegationRuleDto record */
export interface DelegationRuleDto {
  id: string;
  delegatorUserId: string;
  delegateUserId: string;
  validFrom: string;
  validUntil: string;
  isActive: boolean;
  tenantId: string;
  createdAt: string;
  createdBy: string;
}

/** POST /api/v1/workflow/tasks/{id}/delegate */
export interface DelegateTaskRequest {
  delegateToUserId: string;
  expiresAt: string;
  reason?: string | null;
}

/** POST /api/v1/workflow/tasks/{id}/return-delegation — no body */
export type ReturnDelegationRequest = Record<string, never>;

/** POST /api/v1/workflow/delegation-rules */
export interface CreateDelegationRuleRequest {
  delegatorUserId: string;
  delegateUserId: string;
  validFrom: string;
  validUntil: string;
}

/** PUT /api/v1/workflow/delegation-rules/{id} */
export interface UpdateDelegationRuleRequest {
  delegateUserId: string;
  validFrom: string;
  validUntil: string;
}

export interface WorkflowInstanceHistoryDto {
  id: string;
  fromStateId: string;
  toStateId: string;
  actionName: string;
  transitionedBy: string;
  comment?: string | null;
  transitionedAt: string;
  fromStateName?: string | null;
  toStateName?: string | null;
  actorName?: string | null;
  actionLabel?: string | null;
}

export interface WorkflowInstanceDto {
  id: string;
  definitionId: string;
  entityType: string;
  entityId: string;
  currentStateId: string;
  tenantId: string;
  startedAt: string;
  completedAt?: string | null;
  dueAt?: string | null;
  currentStateEnteredAt: string;
  history: WorkflowInstanceHistoryDto[];
}

export interface ExecuteTransitionRequest {
  transitionId: string;
  comment?: string | null;
  metadata?: object | null;
}

export interface ReassignTaskRequest {
  newAssigneeId: string;
  reason?: string | null;
}

// ─── Batch transition types ────────────────────────────────────────────────────

export interface BatchTransitionItem {
  instanceId: string;
  transitionId: string;
}

export interface BatchTransitionRequest {
  items: BatchTransitionItem[];
  comment?: string | null;
}

export interface BatchTransitionItemResult {
  instanceId: string;
  transitionId: string;
  success: boolean;
  error?: string | null;
  resultInstance?: WorkflowInstanceDto | null;
}

export interface BatchTransitionResult {
  totalCount: number;
  successCount: number;
  failureCount: number;
  items: BatchTransitionItemResult[];
}

// ─── Migration types (T5-10) ──────────────────────────────────────────────────

/** BE enum MigrationStatus — serialized as PascalCase string via JsonStringEnumConverter */
export type MigrationStatus =
  | 'Draft'
  | 'PendingApproval'
  | 'Approved'
  | 'InProgress'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

/** BE enum MigrationItemStatus — serialized as PascalCase string */
export type MigrationItemStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Skipped';

/** Mirrors BE VersionHistoryDto — Dapper-hydrated, camelCase via JsonNamingPolicy */
export interface VersionHistoryDto {
  id: string;
  definitionKey: string;
  version: number;
  isLatest: boolean;
  isActive: boolean;
  createdAt: string;
  instanceCount: number;
}

/** Mirrors BE MigrationDto */
export interface MigrationDto {
  id: string;
  sourceDefinitionId: string;
  targetDefinitionId: string;
  status: MigrationStatus;
  mappingHash: string;
  totalInstances: number;
  migratedCount: number;
  failedCount: number;
  skippedCount: number;
  createdBy: string;
  approvedBy?: string | null;
  approvedAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  hangfireJobId?: string | null;
  hangfireJobState?: string | null;
}

/** Mirrors BE MigrationItemDto */
export interface MigrationItemDto {
  id: string;
  instanceId: string;
  oldStateId: string;
  newStateId: string;
  status: MigrationItemStatus;
  errorMessage?: string | null;
  processedAt?: string | null;
}

/** Mirrors BE StateMappingDto */
export interface StateMappingDto {
  oldStateId: string;
  oldStateName: string;
  newStateId: string;
  newStateName: string;
}

/** Mirrors BE UnmappedStateDto */
export interface UnmappedStateDto {
  stateId: string;
  stateName: string;
  activeInstanceCount: number;
}

/** Mirrors BE MigrationValidationResult */
export interface MigrationValidationResult {
  isValid: boolean;
  errors: string[];
  unmappedStates: UnmappedStateDto[];
}

/** Request body for validate + create migration */
export interface CreateMigrationRequest {
  sourceDefinitionId: string;
  targetDefinitionId: string;
  mappings: StateMappingDto[];
  allowDowngrade: boolean;
}

// ─── Analytics types (T5-09) ───────────────────────────────────────────────────

/** Maps to BE enum GroupByPeriod — used for time-series grouping in analytics queries */
export type GroupByPeriod = 'Day' | 'Week' | 'Month';

/** Count of workflow instances per state for a given definition */
export interface StateDistributionDto {
  stateId: string;
  stateName: string;
  count: number;
  percentage: number;
}

/** Average and median time (minutes) spent in each state */
export interface StateProcessingTimeDto {
  stateId: string;
  stateName: string;
  avgMinutes: number;
  medianMinutes: number;
  sampleCount: number;
}

/** Transition count per user per time period.
 *  When isOthers=true this row is the aggregated "Others" bucket (userId=="00000000-0000-0000-0000-000000000000"). */
export interface UserThroughputDto {
  userId: string;
  period: string;
  transitionCount: number;
  /** True when this row aggregates all users outside the top-5 */
  isOthers: boolean;
}

/** SLA on-time compliance rate per time period */
export interface SlaComplianceTrendDto {
  period: string;
  total: number;
  onTime: number;
  compliancePercent: number;
}

/** Instances that have been in a state longer than a threshold */
export interface AgingInstanceDto {
  instanceId: string;
  definitionId: string;
  stateName: string;
  hoursInState: number;
  enteredAt: string;
}

/** States with high average dwell time (bottleneck candidates) */
export interface BottleneckDto {
  stateId: string;
  stateName: string;
  avgDwellMinutes: number;
  activeCount: number;
}

/** Filter params shared across analytics queries */
export interface AnalyticsFilterParams {
  definitionId?: string;
  from?: string;
  to?: string;
  groupBy?: GroupByPeriod;
}

// ─── Cursor pagination types (T5-16) ──────────────────────────────────────────

/** PagedResult returned by cursor-based list endpoints — totalCount is null in cursor mode */
export interface CursorPagedResult<T> {
  items: T[];
  totalCount: null;
  meta: {
    nextCursor: string | null;
    hasNextPage: boolean;
  };
}

/** Mirrors BE WorkflowInstanceListItemDto — thin list projection for cursor-paginated dashboard */
export interface WorkflowInstanceListItem {
  id: string;
  definitionId: string;
  entityType: string;
  entityId: string;
  currentStateId: string;
  startedAt: string;
  dueAt: string | null;
  completedAt: string | null;
}

/** Mirrors BE WorkflowInstanceSummaryDto — KPI counts detached from paged window */
export interface WorkflowInstanceSummary {
  active: number;
  completedToday: number;
  slaBreachRate: number | null;
  total: number;
}

/** Status filter for instance list — 'all' means omit the param on the API call */
export type InstanceStatus = 'active' | 'completed' | 'all';

/** Filter params for workflow instance list and summary hooks */
export interface InstanceFilter {
  entityType?: string;
  stateId?: string;
  status?: InstanceStatus;
}
