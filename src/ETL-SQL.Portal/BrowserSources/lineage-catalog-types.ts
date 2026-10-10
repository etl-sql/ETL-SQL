import type { DagGraphInput, DagHandle } from '../designer/dag.js';
import type { Metrics } from './operations-responses.js';

export type CatalogMode = 'history' | 'stewardship' | 'protected' | 'audit' | 'impact';
export interface SavedLineageView {
  name: string; kind: string; query: string; column: string; tagValue: string; from: string; to: string;
}
export interface StewardshipQuery {
  view: string; q: string; steward: string; domain: string; staleAfterDays: number; limit: number;
}
export interface LineageHistoryQuery {
  limit: number; from: string | null; to: string | null; key?: string; value?: string; path?: string; name?: string; column?: string;
}
export interface ProtectedResult {
  protectedRows: ProtectedLineageHistoryEntry[] | null; suggestions: ProtectedDataSuggestionEntry[] | null;
  errors: string[];
}
export interface StewardAuditResult extends ProtectedResult {
  stewardship: StewardshipCatalogDto | null; auditEvents: AuditLogDto[] | null; operational: Metrics | null;
}
export interface LineageCatalogState {
  mode: CatalogMode; kind: string; query: string; column: string; tagValue: string; from: string; to: string;
  limit: number; rows: CatalogLineageHistoryDto[]; savedViews: SavedLineageView[]; selectedView: string;
  view: 'table' | 'graph'; stewardshipView: string; stewardshipQuery: string; stewardshipSteward: string;
  stewardshipDomain: string; stewardshipStaleDays: number; stewardship: StewardshipCatalogDto | null;
  impactKind: string; impactName: string; impactColumn: string; impactDirection: string; impactDepth: number;
  impact: LineageImpactDto | null; auditLimit: number; stewardAudit: StewardAuditResult | null;
  protectedData: ProtectedResult | null;
}
export interface LineageCatalogOptions {
  host: HTMLElement | null;
  catalogApi: {
    lineage(kind: string, query: LineageHistoryQuery, signal?: AbortSignal): Promise<unknown>;
    stewardship(query: StewardshipQuery, signal?: AbortSignal): Promise<unknown>;
    impact(query: LineageImpactRequestDto, signal?: AbortSignal): Promise<unknown>;
    protectedData?(query: { limit: number }, signal?: AbortSignal): Promise<unknown>;
    protectedDataSuggestions?(query: { limit: number }, signal?: AbortSignal): Promise<unknown>;
  };
  adminApi?: {
    auditLog?(page: number, pageSize: number, action: string, userId?: number | string,
      resourceType?: string, resourceId?: string, signal?: AbortSignal): Promise<unknown>;
    operationalMetrics?(signal?: AbortSignal): Promise<unknown>;
  };
  renderDag(container: HTMLElement, graph: DagGraphInput): Pick<DagHandle, 'dispose'>;
  renderLineageRow(row: CatalogLineageHistoryDto, helpers: {
    timeAgo: (value: string | null | undefined) => string;
    formatBuiltAt: (value: string | null | undefined) => string;
  }): string;
  lineageRowsToCsv(rows: CatalogLineageHistoryDto[]): string;
  openReport?(id: number): void;
  timeAgo?(value: string | null | undefined): string;
  formatBuiltAt?(value: string | null | undefined): string;
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  prepare?(mode: CatalogMode): void; onModeChange?(mode: CatalogMode): void; allowAudit?: boolean;
  promptFn?(message: string, value: string): string | null | Promise<string | null>;
  viewKey?: string;
}
