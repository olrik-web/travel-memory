import { skipToken, useQuery } from '@tanstack/react-query';
import { getPhotoImport, getPhotoTimeline } from './photoImportApi';
import { isTerminalBatch } from './photoImportStatus';

const statusPollIntervalMs = 1500;

export const photoKeys = {
  timeline: (tripId: string) => ['trips', tripId, 'photos'] as const,
  importBatch: (batchId: string) => ['photo-imports', batchId] as const,
};

export function usePhotoTimeline(tripId: string | undefined) {
  return useQuery({
    queryKey: photoKeys.timeline(tripId ?? ''),
    queryFn: tripId ? ({ signal }) => getPhotoTimeline(tripId, signal) : skipToken,
  });
}

// Polls the batch while the worker is still processing it. Polling stops once the batch is
// complete, or when it could not be loaded at all. Polling is the only refresh: refetching
// on focus or reconnect would retry a batch that failed to load and flash the loading state.
export function usePhotoImportBatch(batchId: string | undefined) {
  return useQuery({
    queryKey: photoKeys.importBatch(batchId ?? ''),
    queryFn: batchId ? ({ signal }) => getPhotoImport(batchId, signal) : skipToken,
    refetchInterval: (query) => {
      const batch = query.state.data;
      if (batch) {
        return isTerminalBatch(batch) ? false : statusPollIntervalMs;
      }

      return query.state.status === 'error' ? false : statusPollIntervalMs;
    },
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
}
