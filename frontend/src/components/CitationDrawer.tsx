import React, { useState, useEffect } from 'react';
import { X, BookOpen, FileText, CheckCircle2 } from 'lucide-react';
import type { Citation } from '../types/rag';

export interface CitationDrawerProps {
  isOpen: boolean;
  citations: Citation[];
  selectedIndex?: number;
  onSelectCitation?: (index: number) => void;
  onClose: () => void;
}

export const CitationDrawer: React.FC<CitationDrawerProps> = ({
  isOpen,
  citations,
  selectedIndex = 0,
  onSelectCitation,
  onClose,
}) => {
  const [internalIndex, setInternalIndex] = useState<number>(selectedIndex);

  useEffect(() => {
    setInternalIndex(selectedIndex);
  }, [selectedIndex]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  const activeIndex = Math.min(internalIndex, Math.max(0, citations.length - 1));
  const activeCitation = citations[activeIndex];

  const handleSelect = (idx: number) => {
    setInternalIndex(idx);
    if (onSelectCitation) {
      onSelectCitation(idx);
    }
  };

  return (
    <div className="rag-drawer-backdrop" onClick={onClose}>
      <aside
        data-testid="citation-drawer"
        className="rag-citation-drawer"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="citation-drawer-title"
      >
        {/* Drawer Header */}
        <div className="rag-drawer-header">
          <div className="rag-drawer-header-left">
            <BookOpen className="rag-drawer-icon" size={20} />
            <div>
              <h3 id="citation-drawer-title" className="rag-drawer-title">
                Verified Source Citations
              </h3>
              <span className="rag-drawer-subtitle">
                {citations.length} grounded reference{citations.length === 1 ? '' : 's'} retrieved
              </span>
            </div>
          </div>
          <button
            type="button"
            data-testid="citation-drawer-close"
            className="rag-drawer-close-btn"
            onClick={onClose}
            aria-label="Close citation drawer"
          >
            <X size={20} />
          </button>
        </div>

        <div className="rag-drawer-body">
          {citations.length === 0 ? (
            <div className="rag-drawer-empty">
              <p>No citations available for this response.</p>
            </div>
          ) : (
            <>
              {/* Citation List */}
              <div className="rag-drawer-list">
                <div className="rag-drawer-section-label">Retrieved Evidence</div>
                {citations.map((c, idx) => {
                  const isSelected = idx === activeIndex;
                  const docTitle = c.documentTitle || c.title || 'Untitled Document';
                  return (
                    <div
                      key={idx}
                      data-testid="citation-drawer-item"
                      className={`rag-citation-item ${
                        isSelected ? 'rag-citation-item--selected' : ''
                      }`}
                      onClick={() => handleSelect(idx)}
                      role="button"
                      tabIndex={0}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          handleSelect(idx);
                        }
                      }}
                    >
                      <div className="rag-citation-item-header">
                        <div className="rag-citation-title-wrapper">
                          <FileText size={16} className="rag-citation-doc-icon" />
                          <span className="rag-citation-item-title">{docTitle}</span>
                        </div>
                        <span className="rag-citation-page-badge">Page {c.pageNumber}</span>
                      </div>
                      <p className="rag-citation-item-snippet">{c.excerpt}</p>
                    </div>
                  );
                })}
              </div>

              {/* Active Citation Excerpt Inspection */}
              {activeCitation && (
                <div className="rag-drawer-inspector">
                  <div className="rag-drawer-section-label">
                    <CheckCircle2 size={14} className="rag-inspector-icon" />
                    Verified Grounded Excerpt (Page {activeCitation.pageNumber})
                  </div>
                  <div className="rag-drawer-quote-box">
                    <blockquote
                      data-testid="citation-drawer-excerpt"
                      className="rag-drawer-excerpt"
                    >
                      "{activeCitation.excerpt}"
                    </blockquote>
                    <div className="rag-drawer-quote-meta">
                      <span>Source: {activeCitation.documentTitle || activeCitation.title}</span>
                      <span>Page: {activeCitation.pageNumber}</span>
                    </div>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </aside>
    </div>
  );
};

export default CitationDrawer;
