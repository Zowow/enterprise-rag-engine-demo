import React, { useState, useEffect, useCallback } from 'react';
import { DollarSign, Cpu, Zap, RefreshCw, Layers } from 'lucide-react';
import { ragClient } from '../api/ragClient';
import type { AuditMetrics } from '../types/rag';

export interface TokenMetricsRibbonProps {
  metrics?: AuditMetrics | null;
  onRefresh?: () => void;
  className?: string;
}

export const TokenMetricsRibbon: React.FC<TokenMetricsRibbonProps> = ({
  metrics: controlledMetrics,
  onRefresh,
  className = '',
}) => {
  const [internalMetrics, setInternalMetrics] = useState<AuditMetrics | null>(
    controlledMetrics || null
  );
  const [isLoading, setIsLoading] = useState<boolean>(false);

  const fetchMetrics = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await ragClient.getAuditMetrics();
      setInternalMetrics(data);
    } catch (err) {
      console.error('Failed to load audit metrics:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (controlledMetrics !== undefined) {
      setInternalMetrics(controlledMetrics);
    } else {
      fetchMetrics();
    }
  }, [controlledMetrics, fetchMetrics]);

  const handleRefresh = async () => {
    if (onRefresh) {
      onRefresh();
    }
    await fetchMetrics();
  };

  const metrics = controlledMetrics !== undefined ? controlledMetrics : internalMetrics;

  const totalQueries = metrics?.totalQueries ?? 0;
  const totalTokens = metrics?.totalTokens ?? 0;
  const totalCostUsd = metrics?.totalCostUsd ?? 0;
  const cacheHitRatio = metrics?.cacheHitRatio ?? 0;

  return (
    <div
      data-testid="token-metrics-ribbon"
      className={`rag-metrics-ribbon ${className}`}
    >
      <div className="rag-metric-item">
        <Layers size={14} className="rag-metric-icon rag-metric-icon--blue" />
        <span className="rag-metric-label">Queries:</span>
        <span className="rag-metric-value">{totalQueries.toLocaleString()}</span>
      </div>

      <div className="rag-metric-divider" />

      <div className="rag-metric-item">
        <Cpu size={14} className="rag-metric-icon rag-metric-icon--purple" />
        <span className="rag-metric-label">Tokens:</span>
        <span className="rag-metric-value">{totalTokens.toLocaleString()}</span>
      </div>

      <div className="rag-metric-divider" />

      <div className="rag-metric-item">
        <DollarSign size={14} className="rag-metric-icon rag-metric-icon--green" />
        <span className="rag-metric-label">Total Cost:</span>
        <span data-testid="metric-total-cost" className="rag-metric-value">
          ${totalCostUsd.toFixed(4)}
        </span>
      </div>

      <div className="rag-metric-divider" />

      <div className="rag-metric-item">
        <Zap size={14} className="rag-metric-icon rag-metric-icon--amber" />
        <span className="rag-metric-label">Cache Hit Ratio:</span>
        <span
          data-testid="metric-cache-hit-ratio"
          className="rag-metric-value rag-metric-value--accent"
        >
          {(cacheHitRatio * 100).toFixed(1)}%
        </span>
      </div>

      <button
        type="button"
        data-testid="metric-refresh-button"
        className="rag-metric-refresh-btn"
        onClick={handleRefresh}
        disabled={isLoading}
        title="Refresh audit metrics"
        aria-label="Refresh audit metrics"
      >
        <RefreshCw size={13} className={isLoading ? 'rag-spinner' : ''} />
      </button>
    </div>
  );
};

export default TokenMetricsRibbon;
