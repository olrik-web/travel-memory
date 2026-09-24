export interface PhotoImportBatch {
  id: string;
  tripId: string;
  state: string;
  timeAdjustmentMinutes?: number;
  counts: PhotoImportCounts;
  items: PhotoImportItem[];
}

export interface PhotoImportCounts {
  total: number;
  awaitingUpload: number;
  analyzing: number;
  ready: number;
  processing: number;
  succeeded: number;
  duplicates: number;
  failed: number;
}

export interface PhotoImportItem {
  id: string;
  clientFileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  state: string;
  outcome: string;
  capturedAtOriginalLocal?: string;
  exifOffsetMinutes?: number;
  capturedAtTimelineLocal?: string;
  errorCode?: string;
  errorMessage?: string;
  originalRetainedUntilUtc?: string;
  originalDeletedAtUtc?: string;
  canRetry: boolean;
  uploadUrl?: string;
  uploadExpiresAtUtc?: string;
}

export interface CreatePhotoImportFile {
  clientFileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface PhotoTimePreview {
  timeAdjustmentMinutes: number;
  items: PhotoTimePreviewItem[];
}

export interface PhotoTimePreviewItem {
  itemId: string;
  fileName: string;
  capturedAtOriginalLocal: string;
  capturedAtTimelineLocal: string;
}

export interface UploadGrant {
  uploadUrl: string;
  expiresAtUtc: string;
}

export interface PhotoTimelineItem {
  id: string;
  fileName: string;
  capturedAtOriginalLocal: string;
  timeAdjustmentMinutes: number;
  capturedAtTimelineLocal: string;
  exifOffsetMinutes?: number;
  thumbnailUrl: string;
  webUrl: string;
  urlsExpireAtUtc: string;
  width: number;
  height: number;
  thumbnailWidth: number;
  thumbnailHeight: number;
}

export interface PhotoTimeline {
  items: PhotoTimelineItem[];
}
