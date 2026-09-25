import type { components } from '../../api/schema';

type Schemas = components['schemas'];

export type PhotoImportBatch = Schemas['PhotoImportBatchResponse'];
export type PhotoImportCounts = Schemas['PhotoImportCountsResponse'];
export type PhotoImportItem = Schemas['PhotoImportItemResponse'];
export type CreatePhotoImportFile = Schemas['CreatePhotoImportFileRequest'];
export type PhotoTimePreview = Schemas['PhotoTimePreviewResponse'];
export type PhotoTimePreviewItem = Schemas['PhotoTimePreviewItemResponse'];
export type UploadGrant = Schemas['UploadGrantResponse'];
export type PhotoTimelineItem = Schemas['PhotoTimelineItemResponse'];
export type PhotoTimeline = Schemas['PhotoTimelineResponse'];
