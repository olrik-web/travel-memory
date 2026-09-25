import type { PhotoImportBatch, PhotoImportItem } from './types';

const terminalStates = new Set(['Completed', 'CompletedWithErrors']);

const stateDescriptions: Record<string, string> = {
  AwaitingUpload: 'Waiting for upload',
  QueuedForAnalysis: 'Waiting for analysis',
  Analyzing: 'Reading EXIF and checking for duplicates',
  ReadyForReview: 'Ready for time preview',
  QueuedForProcessing: 'Waiting for processing',
  Processing: 'Creating web copy and thumbnail',
  CleanupPending: 'Verified - cleaning up the original',
  Failed: 'Could not be processed',
  CleanupFailed: 'Imported, but cleanup needs attention',
};

export function isTerminalBatch(batch: PhotoImportBatch) {
  return terminalStates.has(batch.state);
}

export function describeItemState(item: PhotoImportItem) {
  if (item.state === 'Succeeded') {
    return item.outcome === 'Duplicate' ? 'Duplicate - not imported again' : 'Imported';
  }

  return stateDescriptions[item.state] ?? item.state;
}

export function formatFileCount(count: number) {
  return count === 1 ? '1 file' : `${count} files`;
}

export function formatLocalDateTime(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}
