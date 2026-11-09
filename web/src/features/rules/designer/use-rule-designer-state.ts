// Local state management for the visual rule designer
// Tracks pending priority changes and field edits before batch save

import { useState, useCallback } from 'react';
import type { UpdateRuleDto } from '../rules-api';

export interface PendingChange {
  ruleId: string;
  changes: UpdateRuleDto;
}

export interface RuleDesignerState {
  isDirty: boolean;
  changeCount: number;
  markPriorityChange: (ruleId: string, newPriority: number) => void;
  markRuleEdit: (ruleId: string, fields: Partial<UpdateRuleDto>) => void;
  getChanges: () => PendingChange[];
  resetChanges: () => void;
}

// useRuleDesignerState — tracks unsaved modifications to rules in the designer
export function useRuleDesignerState(): RuleDesignerState {
  const [modifiedRules, setModifiedRules] = useState<Map<string, UpdateRuleDto>>(new Map());

  const markPriorityChange = useCallback((ruleId: string, newPriority: number) => {
    setModifiedRules((prev) => {
      const next = new Map(prev);
      const existing = next.get(ruleId) ?? {};
      next.set(ruleId, { ...existing, priority: newPriority });
      return next;
    });
  }, []);

  const markRuleEdit = useCallback((ruleId: string, fields: Partial<UpdateRuleDto>) => {
    setModifiedRules((prev) => {
      const next = new Map(prev);
      const existing = next.get(ruleId) ?? {};
      next.set(ruleId, { ...existing, ...fields });
      return next;
    });
  }, []);

  const getChanges = useCallback((): PendingChange[] => {
    return Array.from(modifiedRules.entries()).map(([ruleId, changes]) => ({ ruleId, changes }));
  }, [modifiedRules]);

  const resetChanges = useCallback(() => {
    setModifiedRules(new Map());
  }, []);

  return {
    isDirty: modifiedRules.size > 0,
    changeCount: modifiedRules.size,
    markPriorityChange,
    markRuleEdit,
    getChanges,
    resetChanges,
  };
}
