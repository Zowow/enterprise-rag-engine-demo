import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { RateLimitBanner } from '../components/RateLimitBanner';
import { CitationDrawer } from '../components/CitationDrawer';
import { TokenMetricsRibbon } from '../components/TokenMetricsRibbon';
import { RagChatInterface } from '../components/RagChatInterface';
import { ragClient, RateLimitError } from '../api/ragClient';
import type { Citation, RagQueryResponseWithMeta, AuditMetrics } from '../types/rag';

describe('RAG Chat & Citation Components', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValue({
      totalQueries: 0,
      totalTokens: 0,
      totalCostUsd: 0,
      cacheHitRatio: 0,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('RateLimitBanner', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('does not render when isVisible is false', () => {
      render(
        <RateLimitBanner
          isVisible={false}
          retryAfterSeconds={30}
        />
      );

      expect(screen.queryByTestId('rate-limit-banner')).not.toBeInTheDocument();
    });

    it('renders banner with active countdown and role="alert"', () => {
      render(
        <RateLimitBanner
          isVisible={true}
          retryAfterSeconds={45}
        />
      );

      const banner = screen.getByTestId('rate-limit-banner');
      expect(banner).toBeInTheDocument();
      expect(banner).toHaveAttribute('role', 'alert');

      const countdown = screen.getByTestId('rate-limit-countdown');
      expect(countdown).toBeInTheDocument();
      expect(countdown).toHaveTextContent('45s');
    });

    it('counts down each second and triggers onCountdownComplete at zero', () => {
      const onComplete = vi.fn();

      render(
        <RateLimitBanner
          isVisible={true}
          retryAfterSeconds={3}
          onCountdownComplete={onComplete}
        />
      );

      const countdown = screen.getByTestId('rate-limit-countdown');
      expect(countdown).toHaveTextContent('3s');

      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(countdown).toHaveTextContent('2s');

      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(countdown).toHaveTextContent('1s');

      act(() => {
        vi.advanceTimersByTime(1000);
      });
      expect(countdown).toHaveTextContent('0s');
      expect(onComplete).toHaveBeenCalledTimes(1);
    });
  });

  describe('CitationDrawer', () => {
    const mockCitations: Citation[] = [
      {
        documentTitle: 'Data Privacy Standard 2026',
        pageNumber: 12,
        excerpt: 'Personal data must be pseudonymized before entering indexing pipeline.',
      },
      {
        documentTitle: 'ISO 27001 Compliance Guidelines',
        pageNumber: 4,
        excerpt: 'Access keys and tokens must rotate every 90 days.',
      },
    ];

    it('does not render contents when isOpen is false', () => {
      render(
        <CitationDrawer
          isOpen={false}
          citations={mockCitations}
          onClose={vi.fn()}
        />
      );

      expect(screen.queryByTestId('citation-drawer')).not.toBeInTheDocument();
    });

    it('renders drawer, citations list, and active excerpt when isOpen is true', () => {
      render(
        <CitationDrawer
          isOpen={true}
          citations={mockCitations}
          selectedIndex={0}
          onClose={vi.fn()}
        />
      );

      expect(screen.getByTestId('citation-drawer')).toBeInTheDocument();
      const items = screen.getAllByTestId('citation-drawer-item');
      expect(items).toHaveLength(2);

      expect(screen.getByText('Data Privacy Standard 2026')).toBeInTheDocument();
      expect(screen.getByText('ISO 27001 Compliance Guidelines')).toBeInTheDocument();

      const excerpt = screen.getByTestId('citation-drawer-excerpt');
      expect(excerpt).toHaveTextContent('Personal data must be pseudonymized');
    });

    it('calls onClose when close button is clicked', () => {
      const onClose = vi.fn();
      render(
        <CitationDrawer
          isOpen={true}
          citations={mockCitations}
          onClose={onClose}
        />
      );

      const closeBtn = screen.getByTestId('citation-drawer-close');
      fireEvent.click(closeBtn);
      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('switches excerpt when another citation is selected', () => {
      const onSelect = vi.fn();
      render(
        <CitationDrawer
          isOpen={true}
          citations={mockCitations}
          selectedIndex={0}
          onSelectCitation={onSelect}
          onClose={vi.fn()}
        />
      );

      const items = screen.getAllByTestId('citation-drawer-item');
      fireEvent.click(items[1]);
      expect(onSelect).toHaveBeenCalledWith(1);
    });
  });

  describe('TokenMetricsRibbon', () => {
    const mockMetrics: AuditMetrics = {
      totalQueries: 128,
      totalTokens: 45200,
      totalCostUsd: 0.0678,
      cacheHitRatio: 0.625,
    };

    it('fetches and renders audit metrics on mount', async () => {
      vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValueOnce(mockMetrics);

      render(<TokenMetricsRibbon />);

      expect(screen.getByTestId('token-metrics-ribbon')).toBeInTheDocument();

      await waitFor(() => {
        expect(screen.getByTestId('metric-total-cost')).toHaveTextContent('$0.0678');
        expect(screen.getByTestId('metric-cache-hit-ratio')).toHaveTextContent('62.5%');
        expect(screen.getByText('128')).toBeInTheDocument();
        expect(screen.getByText('45,200')).toBeInTheDocument();
      });
    });

    it('refreshes metrics when refresh button is clicked', async () => {
      const metricsSpy = vi
        .spyOn(ragClient, 'getAuditMetrics')
        .mockResolvedValue(mockMetrics);

      render(<TokenMetricsRibbon />);

      await waitFor(() => {
        expect(metricsSpy).toHaveBeenCalledTimes(1);
      });

      const refreshBtn = screen.getByTestId('metric-refresh-button');
      fireEvent.click(refreshBtn);

      await waitFor(() => {
        expect(metricsSpy).toHaveBeenCalledTimes(2);
      });
    });
  });

  describe('RagChatInterface', () => {
    const mockQueryResponse: RagQueryResponseWithMeta = {
      result: {
        answer: 'All corporate data must be encrypted with AES-256 at rest.\n\n- Key rotation required annually.',
        citations: [
          {
            documentTitle: 'Security Policy Handbook',
            pageNumber: 8,
            excerpt: 'All disks in production cluster must enforce AES-256 encryption.',
          },
        ],
        isCached: false,
        costUsd: 0.0014,
      },
      isSemanticCacheHit: false,
    };

    it('renders query input, submit button, and empty state initially', async () => {
      render(<RagChatInterface />);

      expect(screen.getByTestId('rag-chat-interface')).toBeInTheDocument();
      expect(screen.getByTestId('query-input')).toBeInTheDocument();
      expect(screen.getByTestId('query-submit-button')).toBeInTheDocument();
      expect(screen.getByTestId('chat-empty-state')).toBeInTheDocument();

      await waitFor(() => {
        expect(screen.getByTestId('token-metrics-ribbon')).toBeInTheDocument();
      });
    });

    it('submits query, renders answer, and displays clickable citation pills', async () => {
      const querySpy = vi
        .spyOn(ragClient, 'queryRag')
        .mockResolvedValueOnce(mockQueryResponse);
      vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValueOnce({
        totalQueries: 1,
        totalTokens: 500,
        totalCostUsd: 0.0014,
        cacheHitRatio: 0,
      });

      render(<RagChatInterface />);

      const input = screen.getByTestId('query-input');
      fireEvent.change(input, { target: { value: 'What encryption standard is required?' } });

      const submitBtn = screen.getByTestId('query-submit-button');
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(querySpy).toHaveBeenCalledWith('What encryption standard is required?');
      });

      expect(
        await screen.findByText(/All corporate data must be encrypted with AES-256 at rest/i)
      ).toBeInTheDocument();

      const citationPills = screen.getAllByTestId('citation-pill');
      expect(citationPills).toHaveLength(1);
      expect(citationPills[0]).toHaveTextContent('Security Policy Handbook, p. 8');

      // Clicking citation pill opens CitationDrawer
      fireEvent.click(citationPills[0]);
      expect(screen.getByTestId('citation-drawer')).toBeInTheDocument();
      expect(screen.getByTestId('citation-drawer-excerpt')).toHaveTextContent(
        'All disks in production cluster must enforce AES-256 encryption.'
      );
    });

    it('displays semantic cache hit badge when response is cached', async () => {
      const cachedResponse: RagQueryResponseWithMeta = {
        result: {
          answer: 'Cached compliance answer regarding token expiration.',
          citations: [],
          isCached: true,
          costUsd: 0.0,
        },
        isSemanticCacheHit: true,
      };

      vi.spyOn(ragClient, 'queryRag').mockResolvedValueOnce(cachedResponse);
      vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValueOnce({
        totalQueries: 2,
        totalTokens: 500,
        totalCostUsd: 0.0014,
        cacheHitRatio: 0.5,
      });

      render(<RagChatInterface />);

      const input = screen.getByTestId('query-input');
      fireEvent.change(input, { target: { value: 'Cached question' } });

      const submitBtn = screen.getByTestId('query-submit-button');
      fireEvent.click(submitBtn);

      expect(await screen.findByTestId('cache-hit-badge')).toBeInTheDocument();
      expect(screen.getByTestId('cache-hit-badge')).toHaveTextContent(/semantic cache hit/i);
    });

    it('handles HTTP 429 RateLimitError by displaying countdown banner and disabling query input', async () => {
      const rateLimitError = new RateLimitError('Rate limit exceeded: 5 requests per 10s', 25);
      vi.spyOn(ragClient, 'queryRag').mockRejectedValueOnce(rateLimitError);
      vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValueOnce({
        totalQueries: 5,
        totalTokens: 2500,
        totalCostUsd: 0.007,
        cacheHitRatio: 0,
      });

      render(<RagChatInterface />);

      const input = screen.getByTestId('query-input');
      fireEvent.change(input, { target: { value: 'Rapid burst query' } });

      const submitBtn = screen.getByTestId('query-submit-button');
      fireEvent.click(submitBtn);

      expect(await screen.findByTestId('rate-limit-banner')).toBeInTheDocument();
      expect(screen.getByTestId('rate-limit-countdown')).toHaveTextContent('25s');
      expect(screen.getByTestId('query-input')).toBeDisabled();
      expect(screen.getByTestId('query-submit-button')).toBeDisabled();
    });
  });
});
