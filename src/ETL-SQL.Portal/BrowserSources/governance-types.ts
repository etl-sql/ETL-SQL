export type GovernanceTab = 'overview' | 'workqueue' | 'exceptions' | 'badges' | 'glossary' | 'quality' | 'settings';
export type GovernanceLoad = 'idle' | 'loading' | 'ready' | 'unauthorized' | 'failed';
export interface GovernanceState {
  mode: 'mine' | 'all';
  tab: GovernanceTab;
  searchFilter: string;
  badgeFilter: string;
  categoryFilter: string;
  load: GovernanceLoad;
  error: string | null;
  mutation: 'idle' | 'pending' | 'unconfirmed';
  mutationMessage: string | null;
  canConfigure: boolean;
  canDecide: boolean;
  dashboard: GovernanceDashboardDto | null;
  findings: StewardshipFindingDto[];
  categories: GovernanceCategoryDto[];
  glossary: StewardshipGlossaryTermDto[];
  settings: StewardshipSettingsDto | null;
  editingTerm: string | null;
  editingCategory: string | null;
  pendingFindingId: number | null;
  pendingDecision: 'ignore' | 'accept-risk' | null;
  pendingVersion: string | null;
  activeDqTrendJob: string | null;
  activeDqTrend: DataQualityTrendDto | null;
  activeDqRules: DataQualityRuleDefinitionDto[];
  activeDqLoading: boolean;
  activeDqError: string | null;
  dqJobs: DataQualityJobDto[];
  dqTrends: Record<string, DataQualityTrendDto | undefined>;
  dqLoading: boolean;
  dqLoad: GovernanceLoad;
  allRules: DataQualityRuleDefinitionDto[];
  quarantineQueue: QuarantineQueueItemDto[];
  allRulesLoaded: boolean;
  dqSearchFilter: string;
}
export interface GovernanceApi {
  dashboard(options?: { scope?: string }, signal?: AbortSignal): Promise<unknown>;
  findings(options?: { status?: string; limit?: number }, signal?: AbortSignal): Promise<unknown>;
  categories(signal?: AbortSignal): Promise<unknown>;
  glossary(signal?: AbortSignal): Promise<unknown>;
  settings(signal?: AbortSignal): Promise<unknown>;
  scan(): Promise<unknown>;
  decideFinding(id: number, body: DecideFindingRequest): Promise<unknown>;
  reviewAsset(body: ReviewAssetRequest): Promise<unknown>;
  assignBadge(body: AssignBadgeRequest): Promise<unknown>;
  removeBadge(body: { assetKey: string; badge: string }): Promise<unknown>;
  saveSettings(body: UpdateStewardshipSettingsRequest): Promise<unknown>;
  saveCategory(body: SaveGovernanceCategoryRequest): Promise<unknown>;
  disableCategory(value: string): Promise<unknown>;
  saveGlossaryTerm(body: SaveGlossaryTermRequest): Promise<unknown>;
  deleteGlossaryTerm(term: string): Promise<unknown>;
}
export interface GovernanceQualityApi {
  qualityJobs(signal?: AbortSignal): Promise<unknown>;
  qualityRulesAll(signal?: AbortSignal): Promise<unknown>;
  quarantineQueue(options?: { limit?: number }, signal?: AbortSignal): Promise<unknown>;
  qualityTrend(options: { jobName: string }, signal?: AbortSignal): Promise<unknown>;
  qualityRules(jobName: string, signal?: AbortSignal): Promise<unknown>;
}
export interface GovernancePortal {
  render(): Promise<void>;
  setTab(tab: string): Promise<void>;
  reload(): Promise<void>;
  dispose(): void;
  state: GovernanceState;
}
