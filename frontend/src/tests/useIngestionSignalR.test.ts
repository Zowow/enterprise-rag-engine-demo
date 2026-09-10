import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import * as signalR from '@microsoft/signalr';
import { useIngestionSignalR } from '../hooks/useIngestionSignalR';
import type { IngestionProgressUpdate } from '../types/rag';

describe('useIngestionSignalR Hook', () => {
  let mockConnection: {
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
    on: ReturnType<typeof vi.fn>;
    off: ReturnType<typeof vi.fn>;
    invoke: ReturnType<typeof vi.fn>;
    onreconnecting: ReturnType<typeof vi.fn>;
    onreconnected: ReturnType<typeof vi.fn>;
    onclose: ReturnType<typeof vi.fn>;
    state: signalR.HubConnectionState;
  };

  let registeredCallbacks: Record<string, (...args: unknown[]) => void> = {};

  beforeEach(() => {
    registeredCallbacks = {};

    mockConnection = {
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
      on: vi.fn((event: string, callback: (...args: unknown[]) => void) => {
        registeredCallbacks[event] = callback;
      }),
      off: vi.fn((event: string) => {
        delete registeredCallbacks[event];
      }),
      invoke: vi.fn().mockResolvedValue(undefined),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
      onclose: vi.fn(),
      state: signalR.HubConnectionState.Connected,
    };

    const mockBuilder = {
      withUrl: vi.fn().mockReturnThis(),
      withAutomaticReconnect: vi.fn().mockReturnThis(),
      configureLogging: vi.fn().mockReturnThis(),
      build: vi.fn().mockReturnValue(mockConnection),
    };

    vi.spyOn(signalR, 'HubConnectionBuilder').mockImplementation(
      () => mockBuilder as unknown as signalR.HubConnectionBuilder
    );
  });

  it('initializes and connects to the SignalR hub', async () => {
    const { result } = renderHook(() =>
      useIngestionSignalR({ hubUrl: 'http://localhost:5000/hubs/ingestion', autoConnect: true })
    );

    // Wait for connection
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockConnection.start).toHaveBeenCalled();
    expect(mockConnection.on).toHaveBeenCalledWith('IngestionProgress', expect.any(Function));
    expect(result.current.connectionState).toBe('Connected');
  });

  it('updates state when receiving IngestionProgress event', async () => {
    const { result } = renderHook(() =>
      useIngestionSignalR({ hubUrl: 'http://localhost:5000/hubs/ingestion', autoConnect: true })
    );

    await act(async () => {
      await Promise.resolve();
    });

    const mockUpdate: IngestionProgressUpdate = {
      jobId: 'job-999',
      status: 'Parsing',
      progressPercentage: 25,
      message: 'Parsing PDF text...',
      timestamp: new Date().toISOString(),
    };

    act(() => {
      if (registeredCallbacks['IngestionProgress']) {
        registeredCallbacks['IngestionProgress'](mockUpdate);
      }
    });

    expect(result.current.latestUpdate).toEqual(mockUpdate);
    expect(result.current.updatesByJobId['job-999']).toEqual(mockUpdate);
  });

  it('allows joining and leaving job groups', async () => {
    const { result } = renderHook(() =>
      useIngestionSignalR({ hubUrl: 'http://localhost:5000/hubs/ingestion', autoConnect: true })
    );

    await act(async () => {
      await Promise.resolve();
    });

    await act(async () => {
      await result.current.joinJob('job-abc');
    });

    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinJobGroup', 'job-abc');

    await act(async () => {
      await result.current.leaveJob('job-abc');
    });

    expect(mockConnection.invoke).toHaveBeenCalledWith('LeaveJobGroup', 'job-abc');
  });

  it('cleans up and stops connection on unmount', async () => {
    const { unmount } = renderHook(() =>
      useIngestionSignalR({ hubUrl: 'http://localhost:5000/hubs/ingestion', autoConnect: true })
    );

    await act(async () => {
      await Promise.resolve();
    });

    unmount();
    expect(mockConnection.stop).toHaveBeenCalled();
  });
});
