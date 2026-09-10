import React, { useState, useRef, DragEvent, ChangeEvent } from 'react';
import { UploadCloud, FileText, X, AlertCircle, Loader2, CheckCircle2 } from 'lucide-react';
import { ragClient } from '../api/ragClient';
import type { DocumentUploadResult } from '../types/rag';

export interface DocumentUploadZoneProps {
  onUploadSuccess?: (result: DocumentUploadResult) => void;
  className?: string;
}

const MAX_FILE_SIZE = 25 * 1024 * 1024; // 25 MB
const ALLOWED_EXTENSIONS = ['.pdf', '.docx', '.md'];

export const DocumentUploadZone: React.FC<DocumentUploadZoneProps> = ({
  onUploadSuccess,
  className = '',
}) => {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [customTitle, setCustomTitle] = useState<string>('');
  const [isDragOver, setIsDragOver] = useState<boolean>(false);
  const [isUploading, setIsUploading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [uploadSuccessMessage, setUploadSuccessMessage] = useState<string | null>(null);

  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const formatFileSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const validateFile = (file: File): boolean => {
    setError(null);
    setUploadSuccessMessage(null);

    const ext = '.' + (file.name.split('.').pop()?.toLowerCase() || '');
    if (!ALLOWED_EXTENSIONS.includes(ext)) {
      setError('Unsupported file format. Please upload PDF, DOCX, or MD.');
      return false;
    }

    if (file.size > MAX_FILE_SIZE) {
      setError('File size exceeds 25MB limit. Please select a smaller file.');
      return false;
    }

    return true;
  };

  const handleFileSelect = (file: File) => {
    if (validateFile(file)) {
      setSelectedFile(file);
      if (!customTitle) {
        setCustomTitle(file.name);
      }
    } else {
      setSelectedFile(null);
    }
  };

  const handleFileInputChange = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      handleFileSelect(file);
    }
  };

  const handleDragOver = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(true);
  };

  const handleDragLeave = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(false);
  };

  const handleDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(false);

    const file = e.dataTransfer.files?.[0];
    if (file) {
      handleFileSelect(file);
    }
  };

  const handleClear = () => {
    setSelectedFile(null);
    setCustomTitle('');
    setError(null);
    setUploadSuccessMessage(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const handleUpload = async () => {
    if (!selectedFile || isUploading) return;

    setIsUploading(true);
    setError(null);
    setUploadSuccessMessage(null);

    try {
      const titleToUse = customTitle.trim() || selectedFile.name;
      const result = await ragClient.uploadDocument(selectedFile, titleToUse);
      setUploadSuccessMessage(`Successfully enqueued "${result.title}" for ingestion.`);
      setSelectedFile(null);
      setCustomTitle('');
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
      if (onUploadSuccess) {
        onUploadSuccess(result);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'An unexpected upload error occurred.';
      setError(msg);
    } finally {
      setIsUploading(false);
    }
  };

  return (
    <div className={`rag-upload-container ${className}`}>
      <div
        data-testid="document-upload-dropzone"
        className={`rag-dropzone ${isDragOver ? 'rag-dropzone--dragover' : ''} ${
          selectedFile ? 'rag-dropzone--has-file' : ''
        }`}
        onDragOver={handleDragOver}
        onDragEnter={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        onClick={() => !selectedFile && fileInputRef.current?.click()}
      >
        <input
          ref={fileInputRef}
          data-testid="document-file-input"
          type="file"
          accept=".pdf,.docx,.md"
          className="rag-file-input"
          onChange={handleFileInputChange}
          style={{ display: 'none' }}
        />

        {!selectedFile ? (
          <div className="rag-dropzone-content">
            <div className="rag-dropzone-icon">
              <UploadCloud size={38} />
            </div>
            <div className="rag-dropzone-text">
              <span className="rag-dropzone-primary-text">
                Click or drag &amp; drop policy document
              </span>
              <span className="rag-dropzone-sub-text">
                Supports PDF, DOCX, or Markdown up to 25MB
              </span>
            </div>
          </div>
        ) : (
          <div className="rag-selected-file-card" onClick={(e) => e.stopPropagation()}>
            <div className="rag-file-icon">
              <FileText size={28} />
            </div>
            <div className="rag-file-details">
              <span className="rag-file-name">{selectedFile.name}</span>
              <span className="rag-file-size">{formatFileSize(selectedFile.size)}</span>
            </div>
            <button
              type="button"
              className="rag-file-remove-btn"
              onClick={handleClear}
              title="Remove file"
              disabled={isUploading}
            >
              <X size={16} />
            </button>
          </div>
        )}
      </div>

      {selectedFile && (
        <div className="rag-upload-fields">
          <label className="rag-field-label" htmlFor="doc-title-input">
            Document Title (Optional)
          </label>
          <input
            id="doc-title-input"
            type="text"
            className="rag-text-input"
            value={customTitle}
            placeholder={selectedFile.name}
            onChange={(e) => setCustomTitle(e.target.value)}
            disabled={isUploading}
          />
        </div>
      )}

      {error && (
        <div className="rag-alert rag-alert--error" role="alert">
          <AlertCircle size={18} />
          <span>{error}</span>
        </div>
      )}

      {uploadSuccessMessage && (
        <div className="rag-alert rag-alert--success" role="status">
          <CheckCircle2 size={18} />
          <span>{uploadSuccessMessage}</span>
        </div>
      )}

      <div className="rag-upload-actions">
        <button
          type="button"
          data-testid="document-upload-button"
          className="rag-btn rag-btn--primary"
          disabled={!selectedFile || isUploading}
          onClick={handleUpload}
        >
          {isUploading ? (
            <>
              <Loader2 size={16} className="rag-spinner" />
              <span>Uploading &amp; Enqueuing...</span>
            </>
          ) : (
            <>
              <UploadCloud size={16} />
              <span>Upload &amp; Index Document</span>
            </>
          )}
        </button>
      </div>
    </div>
  );
};

export default DocumentUploadZone;
