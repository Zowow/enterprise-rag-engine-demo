import React, { useState, useEffect } from 'react';
import { AlertTriangle, Clock } from 'lucide-react';

export interface RateLimitBannerProps {
  isVisible: boolean;
  retryAfterSeconds: number;
  message?: string;
  onCountdownComplete?: () => void;
  className?: string;
}

export const RateLimitBanner: React.FC<RateLimitBannerProps> = ({
  isVisible,
  retryAfterSeconds,
  message,
  onCountdownComplete,
  className = '',
}) => {
  const [timeLeft, setTimeLeft] = useState<number>(retryAfterSeconds);

  useEffect(() => {
    setTimeLeft(retryAfterSeconds);
  }, [retryAfterSeconds, isVisible]);

  useEffect(() => {
    if (!isVisible || timeLeft <= 0) {
      return;
    }

    const interval = setInterval(() => {
      setTimeLeft((prev) => {
        if (prev <= 1) {
          clearInterval(interval);
          if (onCountdownComplete) {
            onCountdownComplete();
          }
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(interval);
  }, [isVisible, timeLeft, onCountdownComplete]);

  if (!isVisible) {
    return null;
  }

  const defaultMessage =
    'Rate limit exceeded (Redis sliding-window: max 5 requests / 10s). System cooldown in effect.';

  return (
    <div
      role="alert"
      data-testid="rate-limit-banner"
      className={`rag-rate-limit-banner ${className}`}
    >
      <div className="rag-rate-limit-content">
        <div className="rag-rate-limit-icon-wrapper">
          <AlertTriangle className="rag-rate-limit-icon" size={18} />
        </div>
        <div className="rag-rate-limit-text">
          <span className="rag-rate-limit-title">429 Too Many Requests</span>
          <p className="rag-rate-limit-desc">{message || defaultMessage}</p>
        </div>
      </div>

      <div className="rag-rate-limit-countdown-pill">
        <Clock size={14} className="rag-countdown-clock-icon" />
        <span>Try again in:</span>
        <span
          data-testid="rate-limit-countdown"
          className="rag-rate-limit-countdown-seconds"
        >
          {timeLeft}s
        </span>
      </div>
    </div>
  );
};

export default RateLimitBanner;
