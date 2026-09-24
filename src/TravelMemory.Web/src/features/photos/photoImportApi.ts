import { requestJson, requestVoid } from '../../api/http';
import type {
  CreatePhotoImportFile,
  PhotoImportBatch,
  PhotoTimeline,
  PhotoTimePreview,
  UploadGrant,
} from './types';

export function createPhotoImport(
  tripId: string,
  clientBatchId: string,
  files: CreatePhotoImportFile[],
) {
  return requestJson<PhotoImportBatch>(`/api/trips/${encodeURIComponent(tripId)}/photo-imports`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientBatchId, files }),
  });
}

export function getPhotoImport(batchId: string, signal?: AbortSignal) {
  return requestJson<PhotoImportBatch>(`/api/photo-imports/${encodeURIComponent(batchId)}`, {
    signal,
  });
}

export function renewUploadGrant(batchId: string, itemId: string) {
  return requestJson<UploadGrant>(
    `/api/photo-imports/${encodeURIComponent(batchId)}/items/${encodeURIComponent(itemId)}/upload-token`,
    { method: 'POST' },
  );
}

export function completePhotoUpload(batchId: string, itemId: string) {
  return requestJson<PhotoImportBatch>(
    `/api/photo-imports/${encodeURIComponent(batchId)}/items/${encodeURIComponent(itemId)}/complete-upload`,
    { method: 'POST' },
  );
}

export function previewPhotoTimes(batchId: string, adjustmentMinutes: number) {
  const query = new URLSearchParams({ adjustmentMinutes: adjustmentMinutes.toString() });
  return requestJson<PhotoTimePreview>(
    `/api/photo-imports/${encodeURIComponent(batchId)}/time-preview?${query}`,
  );
}

export function finalizePhotoImport(batchId: string, timeAdjustmentMinutes: number) {
  return requestJson<PhotoImportBatch>(
    `/api/photo-imports/${encodeURIComponent(batchId)}/finalize`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ timeAdjustmentMinutes }),
    },
  );
}

export function retryPhotoImportItem(batchId: string, itemId: string) {
  return requestVoid(
    `/api/photo-imports/${encodeURIComponent(batchId)}/items/${encodeURIComponent(itemId)}/retry`,
    { method: 'POST' },
  );
}

export function getPhotoTimeline(tripId: string, signal?: AbortSignal) {
  return requestJson<PhotoTimeline>(`/api/trips/${encodeURIComponent(tripId)}/photos`, {
    signal,
  });
}
