import { describe, expect, it } from 'vitest';
import { describeItemState, formatFileCount, isTerminalBatch } from './photoImportStatus';
import type { PhotoImportBatch, PhotoImportItem } from './types';

function item(state: string, outcome = 'None'): PhotoImportItem {
  return {
    id: 'item',
    clientFileId: 'a'.repeat(64),
    fileName: 'photo.jpg',
    contentType: 'image/jpeg',
    sizeBytes: 1,
    state,
    outcome,
    capturedAtOriginalLocal: null,
    exifOffsetMinutes: null,
    capturedAtTimelineLocal: null,
    errorCode: null,
    errorMessage: null,
    originalRetainedUntilUtc: null,
    originalDeletedAtUtc: null,
    canRetry: false,
    uploadUrl: null,
    uploadExpiresAtUtc: null,
  };
}

describe('photo import status', () => {
  it('distinguishes imported photos from duplicates', () => {
    expect(describeItemState(item('Succeeded'))).toBe('Imported');
    expect(describeItemState(item('Succeeded', 'Duplicate'))).toBe(
      'Duplicate - not imported again',
    );
  });

  it('falls back to the raw state for unknown states', () => {
    expect(describeItemState(item('SomethingNew'))).toBe('SomethingNew');
  });

  it('pluralizes file counts', () => {
    expect(formatFileCount(1)).toBe('1 file');
    expect(formatFileCount(0)).toBe('0 files');
    expect(formatFileCount(2)).toBe('2 files');
  });

  it('treats completed batches as terminal', () => {
    const batch = { state: 'CompletedWithErrors' } as PhotoImportBatch;

    expect(isTerminalBatch(batch)).toBe(true);
    expect(isTerminalBatch({ ...batch, state: 'Processing' })).toBe(false);
  });
});
