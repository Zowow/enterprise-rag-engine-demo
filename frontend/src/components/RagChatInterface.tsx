import React, { useState, useRef, useEffect } from 'react';
import {
  Send,
  Sparkles,
  Bot,
  User,
  Zap,
  DollarSign,
  FileText,
  AlertCircle,
  HelpCircle,
  ArrowRight,
} from 'lucide-react';
import { ragClient, RateLimitError } from '../api/ragClient';
import { RateLimitBanner } from './RateLimitBanner';
import { CitationDrawer } from './CitationDrawer';
import { TokenMetricsRibbon } from './TokenMetricsRibbon';
import type { Citation, RagQueryResponseWithMeta } from '../types/rag';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citations?: Citation[];
  isCached?: boolean;
  costUsd?: number;
  timestamp: string;
}

export interface RagChatInterfaceProps {
  className?: string;
  onQueryComplete?: (response: RagQueryResponseWithMeta) => void;
}

export const RagChatInterface: React.FC<RagChatInterfaceProps> = ({
  className = '',
  onQueryComplete,
}) => {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [queryInput, setQueryInput] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [queryError, setQueryError] = useState<string | null>(null);

  // Rate Limiting State
  const [rateLimitState, setRateLimitState] = useState<{
    isLimited: boolean;
    retryAfterSeconds: number;
    message?: string;
  } | null>(null);

  // Citation Drawer State
  const [drawerOpen, setDrawerOpen] = useState<boolean>(false);
  const [activeCitations, setActiveCitations] = useState<Citation[]>([]);
  const [selectedCitationIndex, setSelectedCitationIndex] = useState<number>(0);

  // Audit Metrics refresh key
  const [metricsRefreshKey, setMetricsRefreshKey] = useState<number>(0);

  const messagesEndRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const scrollToBottom = () => {
    if (typeof messagesEndRef.current?.scrollIntoView === 'function') {
      messagesEndRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages, isLoading]);

  const handleOpenCitation = (citations: Citation[], index: number = 0) => {
    setActiveCitations(citations);
    setSelectedCitationIndex(index);
    setDrawerOpen(true);
  };

  const handleCountdownComplete = () => {
    setRateLimitState(null);
    if (inputRef.current) {
      inputRef.current.focus();
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const query = queryInput.trim();
    if (!query || isLoading || rateLimitState?.isLimited) {
      return;
    }

    setQueryError(null);
    const userMessageId = `user-${Date.now()}`;
    const userMessage: ChatMessage = {
      id: userMessageId,
      role: 'user',
      content: query,
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    };

    setMessages((prev) => [...prev, userMessage]);
    setQueryInput('');
    setIsLoading(true);

    try {
      const response = await ragClient.queryRag(query);

      const assistantMessage: ChatMessage = {
        id: `asst-${Date.now()}`,
        role: 'assistant',
        content: response.result.answer,
        citations: response.result.citations || [],
        isCached: response.isSemanticCacheHit || response.result.isCached,
        costUsd: response.result.costUsd,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      };

      setMessages((prev) => [...prev, assistantMessage]);
      setMetricsRefreshKey((k) => k + 1);

      if (onQueryComplete) {
        onQueryComplete(response);
      }
    } catch (err: unknown) {
      if (err instanceof RateLimitError || (err as any)?.isRateLimited) {
        const rateLimitErr = err as RateLimitError;
        setRateLimitState({
          isLimited: true,
          retryAfterSeconds: rateLimitErr.retryAfterSeconds || 60,
          message: rateLimitErr.message,
        });
      } else {
        const message = err instanceof Error ? err.message : 'Failed to retrieve answer.';
        setQueryError(message);
      }
    } finally {
      setIsLoading(false);
    }
  };

  const renderSimpleMarkdown = (text: string) => {
    const lines = text.split('\n');
    return (
      <div className="rag-markdown-body">
        {lines.map((line, idx) => {
          const trimmed = line.trim();
          if (!trimmed) {
            return <div key={idx} className="rag-markdown-spacer" />;
          }

          // Heading
          if (trimmed.startsWith('### ')) {
            return (
              <h4 key={idx} className="rag-markdown-h4">
                {trimmed.slice(4)}
              </h4>
            );
          }
          if (trimmed.startsWith('## ')) {
            return (
              <h3 key={idx} className="rag-markdown-h3">
                {trimmed.slice(3)}
              </h3>
            );
          }
          if (trimmed.startsWith('# ')) {
            return (
              <h2 key={idx} className="rag-markdown-h2">
                {trimmed.slice(2)}
              </h2>
            );
          }

          // Bullet List
          if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
            return (
              <div key={idx} className="rag-markdown-bullet">
                <span className="rag-bullet-dot" />
                <span>{renderFormattedInline(trimmed.slice(2))}</span>
              </div>
            );
          }

          // Numbered List
          const matchNum = trimmed.match(/^(\d+)\.\s+(.*)$/);
          if (matchNum) {
            return (
              <div key={idx} className="rag-markdown-num-item">
                <span className="rag-num-prefix">{matchNum[1]}.</span>
                <span>{renderFormattedInline(matchNum[2])}</span>
              </div>
            );
          }

          // Standard paragraph
          return (
            <p key={idx} className="rag-markdown-p">
              {renderFormattedInline(trimmed)}
            </p>
          );
        })}
      </div>
    );
  };

  const renderFormattedInline = (content: string) => {
    // Basic bold parsing: **text**
    const parts = content.split(/(\*\*.*?\*\*|`.*?`)/g);
    return parts.map((part, i) => {
      if (part.startsWith('**') && part.endsWith('**')) {
        return <strong key={i}>{part.slice(2, -2)}</strong>;
      }
      if (part.startsWith('`') && part.endsWith('`')) {
        return <code key={i} className="rag-inline-code">{part.slice(1, -1)}</code>;
      }
      return part;
    });
  };

  const exampleQueries = [
    'What is our company data retention and encryption standard?',
    'What are the mandatory compliance audit requirements?',
    'How often must administrator access tokens be rotated?',
  ];

  return (
    <div
      data-testid="rag-chat-interface"
      className={`rag-chat-container ${className}`}
    >
      {/* Top Enterprise Metrics Ribbon */}
      <TokenMetricsRibbon key={metricsRefreshKey} className="rag-chat-metrics-ribbon" />

      {/* HTTP 429 Rate Limit Countdown Banner */}
      <RateLimitBanner
        isVisible={!!rateLimitState?.isLimited}
        retryAfterSeconds={rateLimitState?.retryAfterSeconds ?? 60}
        message={rateLimitState?.message}
        onCountdownComplete={handleCountdownComplete}
      />

      {/* Chat Messages Stream */}
      <div className="rag-chat-messages">
        {messages.length === 0 ? (
          <div data-testid="chat-empty-state" className="rag-chat-empty">
            <div className="rag-chat-empty-icon-wrap">
              <Sparkles className="rag-chat-empty-icon" size={32} />
            </div>
            <h3 className="rag-chat-empty-title">Enterprise Knowledge Assistant</h3>
            <p className="rag-chat-empty-subtitle">
              Ask compliance, policy, and architecture questions grounded in verified
              uploaded corporate documents with source citations.
            </p>

            <div className="rag-prompt-suggestions">
              <span className="rag-prompt-suggestions-title">
                <HelpCircle size={14} /> Suggested Prompts:
              </span>
              <div className="rag-suggestion-pills">
                {exampleQueries.map((prompt, idx) => (
                  <button
                    key={idx}
                    type="button"
                    className="rag-suggestion-btn"
                    disabled={isLoading || rateLimitState?.isLimited}
                    onClick={() => {
                      setQueryInput(prompt);
                      inputRef.current?.focus();
                    }}
                  >
                    <span>{prompt}</span>
                    <ArrowRight size={13} />
                  </button>
                ))}
              </div>
            </div>
          </div>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              className={`rag-chat-bubble-row rag-chat-bubble-row--${msg.role}`}
            >
              <div className={`rag-chat-avatar rag-chat-avatar--${msg.role}`}>
                {msg.role === 'assistant' ? <Bot size={18} /> : <User size={18} />}
              </div>

              <div className={`rag-chat-bubble rag-chat-bubble--${msg.role}`}>
                <div className="rag-chat-bubble-header">
                  <span className="rag-chat-sender">
                    {msg.role === 'assistant' ? 'Enterprise Intelligence' : 'You'}
                  </span>
                  <span className="rag-chat-timestamp">{msg.timestamp}</span>
                </div>

                {msg.role === 'assistant' ? (
                  <>
                    <div className="rag-chat-answer">
                      {renderSimpleMarkdown(msg.content)}
                    </div>

                    {/* Meta Bar: Cache status & Cost */}
                    <div className="rag-chat-answer-meta">
                      {msg.isCached && (
                        <span
                          data-testid="cache-hit-badge"
                          className="rag-badge rag-badge--cache-hit"
                        >
                          <Zap size={12} />
                          Semantic Cache HIT ($0.0000)
                        </span>
                      )}

                      {!msg.isCached && msg.costUsd !== undefined && (
                        <span className="rag-badge rag-badge--cost">
                          <DollarSign size={12} />
                          Cost: ${msg.costUsd.toFixed(4)}
                        </span>
                      )}
                    </div>

                    {/* Clickable Citation Pills */}
                    {msg.citations && msg.citations.length > 0 && (
                      <div className="rag-citation-pills-container">
                        <span className="rag-citations-label">
                          <FileText size={13} /> Grounded Sources:
                        </span>
                        <div className="rag-citation-pills-list">
                          {msg.citations.map((c, idx) => {
                            const docTitle = c.documentTitle || c.title || 'Source';
                            return (
                              <button
                                key={idx}
                                type="button"
                                data-testid="citation-pill"
                                className="rag-citation-pill"
                                onClick={() => handleOpenCitation(msg.citations!, idx)}
                                title={`Inspect citation from ${docTitle} (Page ${c.pageNumber})`}
                              >
                                <span className="rag-citation-pill-title">
                                  {docTitle}, p. {c.pageNumber}
                                </span>
                              </button>
                            );
                          })}
                        </div>
                      </div>
                    )}
                  </>
                ) : (
                  <p className="rag-chat-user-text">{msg.content}</p>
                )}
              </div>
            </div>
          ))
        )}

        {/* Loading Indicator */}
        {isLoading && (
          <div className="rag-chat-bubble-row rag-chat-bubble-row--assistant">
            <div className="rag-chat-avatar rag-chat-avatar--assistant">
              <Bot size={18} />
            </div>
            <div className="rag-chat-bubble rag-chat-bubble--assistant rag-chat-loading-bubble">
              <div className="rag-typing-dots">
                <span className="rag-typing-dot" />
                <span className="rag-typing-dot" />
                <span className="rag-typing-dot" />
              </div>
              <span className="rag-loading-text">
                Retrieving Qdrant vectors &amp; synthesizing answer...
              </span>
            </div>
          </div>
        )}

        {/* Error notification if any */}
        {queryError && (
          <div className="rag-chat-error-banner">
            <AlertCircle size={16} />
            <span>{queryError}</span>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      {/* Query Input Box */}
      <form onSubmit={handleSubmit} className="rag-query-form">
        <div className="rag-query-input-wrapper">
          <input
            ref={inputRef}
            type="text"
            data-testid="query-input"
            className="rag-query-input"
            placeholder={
              rateLimitState?.isLimited
                ? `Rate limited. Please wait ${rateLimitState.retryAfterSeconds}s...`
                : 'Ask a question about your verified documents...'
            }
            value={queryInput}
            onChange={(e) => setQueryInput(e.target.value)}
            disabled={isLoading || rateLimitState?.isLimited}
          />
          <button
            type="submit"
            data-testid="query-submit-button"
            className="rag-btn rag-btn--primary rag-query-submit-btn"
            disabled={!queryInput.trim() || isLoading || rateLimitState?.isLimited}
          >
            <Send size={16} />
            <span>Send</span>
          </button>
        </div>
      </form>

      {/* Slide-out Citation Drawer */}
      <CitationDrawer
        isOpen={drawerOpen}
        citations={activeCitations}
        selectedIndex={selectedCitationIndex}
        onSelectCitation={(idx) => setSelectedCitationIndex(idx)}
        onClose={() => setDrawerOpen(false)}
      />
    </div>
  );
};

export default RagChatInterface;
