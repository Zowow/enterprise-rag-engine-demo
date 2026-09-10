import { useState, useEffect, useRef, useCallback } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type { IngestionProgressUpdate } from '../types/rag';

export type SignalRConnectionStatus =
  | 'Disconnected'
  | 'Connecting'
  | 'Connected'
  | 'Reconnecting';

export interface UseIngestionSignalROptions {
  hubUrl?: string;
  autoConnect?: boolean;
  onProgress?: (update: IngestionProgressUpdate) => void;
}

export interface UseIngestionSignalRReturn {
  connectionState: SignalRConnectionStatus;
  latestUpdate: IngestionProgressUpdate | null;
  updatesByJobId: Record<string, IngestionProgressUpdate>;
  error: Error | null;
  joinJob: (jobId: string) => Promise<void>;
  leaveJob: (jobId: string) => Promise<void>;
  connect: () => Promise<void>;
  disconnect: () => Promise<void>;
  hubConnection: HubConnection | null;
}

export function useIngestionSignalR(
  options: UseIngestionSignalROptions = {}
): UseIngestionSignalRReturn {
  const { hubUrl, autoConnect = true, onProgress } = options;

  const resolvedUrl =
    hubUrl ||
    (typeof import.meta !== 'undefined' && import.meta.env?.VITE_SIGNALR_URL
      ? import.meta.env.VITE_SIGNALR_URL
      : 'http://localhost:5000/hubs/ingestion');

  const [connectionState, setConnectionState] =
    useState<SignalRConnectionStatus>('Disconnected');
  const [latestUpdate, setLatestUpdate] = useState<IngestionProgressUpdate | null>(
    null
  );
  const [updatesByJobId, setUpdatesByJobId] = useState<
    Record<string, IngestionProgressUpdate>
  >({});
  const [error, setError] = useState<Error | null>(null);

  const connectionRef = useRef<HubConnection | null>(null);
  const onProgressRef = useRef(onProgress);
  onProgressRef.current = onProgress;

  const mapHubState = (state: HubConnectionState): SignalRConnectionStatus => {
    switch (state) {
      case HubConnectionState.Connected:
        return 'Connected';
      case HubConnectionState.Connecting:
        return 'Connecting';
      case HubConnectionState.Reconnecting:
        return 'Reconnecting';
      default:
        return 'Disconnected';
    }
  };

  const handleUpdate = useCallback((update: IngestionProgressUpdate) => {
    setLatestUpdate(update);
    if (update.jobId) {
      setUpdatesByJobId((prev) => ({
        ...prev,
        [update.jobId]: update,
      }));
    }
    if (onProgressRef.current) {
      onProgressRef.current(update);
    }
  }, []);

  const connect = useCallback(async () => {
    if (
      connectionRef.current &&
      connectionRef.current.state === HubConnectionState.Connected
    ) {
      return;
    }

    try {
      setConnectionState('Connecting');
      setError(null);

      const builder = new HubConnectionBuilder()
        .withUrl(resolvedUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .configureLogging(LogLevel.Warning);

      const connection = builder.build();
      connectionRef.current = connection;

      connection.on('IngestionProgress', (update: IngestionProgressUpdate) => {
        handleUpdate(update);
      });

      connection.onreconnecting(() => {
        setConnectionState('Reconnecting');
      });

      connection.onreconnected(() => {
        setConnectionState('Connected');
      });

      connection.onclose((err) => {
        setConnectionState('Disconnected');
        if (err) {
          setError(err);
        }
      });

      await connection.start();
      setConnectionState(mapHubState(connection.state));
    } catch (err: unknown) {
      setConnectionState('Disconnected');
      const errObj =
        err instanceof Error
          ? err
          : new Error('Failed to connect to Ingestion SignalR Hub.');
      setError(errObj);
    }
  }, [resolvedUrl, handleUpdate]);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      try {
        await connectionRef.current.stop();
      } finally {
        connectionRef.current = null;
        setConnectionState('Disconnected');
      }
    }
  }, []);

  const joinJob = useCallback(async (jobId: string) => {
    if (!jobId) return;
    if (
      connectionRef.current &&
      connectionRef.current.state === HubConnectionState.Connected
    ) {
      await connectionRef.current.invoke('JoinJobGroup', jobId);
    }
  }, []);

  const leaveJob = useCallback(async (jobId: string) => {
    if (!jobId) return;
    if (
      connectionRef.current &&
      connectionRef.current.state === HubConnectionState.Connected
    ) {
      await connectionRef.current.invoke('LeaveJobGroup', jobId);
    }
  }, []);

  useEffect(() => {
    if (autoConnect) {
      connect();
    }

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop().catch(() => {});
        connectionRef.current = null;
      }
    };
  }, [autoConnect, connect]);

  return {
    connectionState,
    latestUpdate,
    updatesByJobId,
    error,
    joinJob,
    leaveJob,
    connect,
    disconnect,
    hubConnection: connectionRef.current,
  };
}

export default useIngestionSignalR;
