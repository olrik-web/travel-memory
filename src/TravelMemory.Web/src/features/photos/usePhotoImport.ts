import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../api/http';
import { runWithConcurrency } from './boundedConcurrency';
import { createFileFingerprint } from './fileFingerprint';
import { clearStoredImport, readStoredImport, saveStoredImport } from './importStorage';
import {
  createPhotoImport,
  finalizePhotoImport,
  previewPhotoTimes,
  retryPhotoImportItem,
} from './photoImportApi';
import { photoKeys, usePhotoImportBatch } from './photoQueries';
import {
  describeRejectedFiles,
  partitionSelection,
  removeDuplicateFingerprints,
  type SkippedFile,
} from './photoSelection';
import type { CreatePhotoImportFile, PhotoImportBatch } from './types';
import { uploadPhotoFile } from './uploadPhotoFile';

const maximumFilesPerImport = 500;
const uploadConcurrency = 4;

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

// A rejected batch lists its problems per file; show them by file name rather than the
// problem's generic title.
function importStartErrorMessage(error: unknown, fileNames: readonly string[]) {
  if (error instanceof ApiError && error.problem?.errors) {
    const details = describeRejectedFiles(error.problem.errors, fileNames);
    if (details.length > 0) {
      return `The import was rejected. ${details.join(' ')}`;
    }
  }

  return errorMessage(error, 'The photo import could not be started.');
}

export function usePhotoImport(tripId: string) {
  const queryClient = useQueryClient();
  const [batchId, setBatchId] = useState(() => readStoredImport(tripId)?.batchId);
  const [error, setError] = useState<string>();
  const [skippedFiles, setSkippedFiles] = useState<SkippedFile[]>([]);
  const [isUploading, setIsUploading] = useState(false);
  const [adjustmentMinutes, setAdjustmentMinutes] = useState(0);

  const batchQuery = usePhotoImportBatch(batchId);
  const batch = batchQuery.data;

  // A stored batch that can no longer be loaded (for example deleted or from another
  // account) is forgotten, so the page offers a fresh import and a reload does not retry it.
  const loadFailed = batchQuery.isError && batch === undefined;
  useEffect(() => {
    if (loadFailed) {
      clearStoredImport(tripId);
    }
  }, [loadFailed, tripId]);

  const queryError = batchQuery.isError
    ? loadFailed
      ? 'The previous import could not be loaded. Start a new import.'
      : 'The status could not be updated. Retrying.'
    : undefined;

  function showBatch(nextBatch: PhotoImportBatch) {
    queryClient.setQueryData(photoKeys.importBatch(nextBatch.id), nextBatch);
  }

  function refreshBatch(id: string) {
    return queryClient.invalidateQueries({ queryKey: photoKeys.importBatch(id) });
  }

  const preview = useMutation({
    mutationFn: (id: string) => previewPhotoTimes(id, adjustmentMinutes),
    onMutate: () => setError(undefined),
    onError: (requestError) =>
      setError(errorMessage(requestError, 'The time preview could not be loaded.')),
  });

  const finalize = useMutation({
    mutationFn: (id: string) => finalizePhotoImport(id, adjustmentMinutes),
    onMutate: () => setError(undefined),
    onSuccess: showBatch,
    onError: (requestError) =>
      setError(errorMessage(requestError, 'The import could not be finalized.')),
  });

  const retry = useMutation({
    mutationFn: ({ id, itemId }: { id: string; itemId: string }) =>
      retryPhotoImportItem(id, itemId),
    onMutate: () => setError(undefined),
    onSuccess: (_, { id }) => refreshBatch(id),
    onError: (requestError) =>
      setError(errorMessage(requestError, 'The file could not be retried.')),
  });

  async function uploadFiles(selectedFiles: File[]) {
    if (selectedFiles.length === 0) {
      return;
    }

    const selection = partitionSelection(selectedFiles);
    setError(undefined);
    setSkippedFiles(selection.skipped);
    if (selection.accepted.length === 0) {
      setError('None of the selected files can be imported. Select JPEG or HEIC photos.');
      return;
    }

    if (selection.accepted.length > maximumFilesPerImport) {
      setError(`Select at most ${maximumFilesPerImport} photos at a time.`);
      return;
    }

    setIsUploading(true);
    let fileNames: string[] = [];

    try {
      const fingerprinted = await Promise.all(
        selection.accepted.map(async (file) => ({
          file,
          fingerprint: await createFileFingerprint(file),
        })),
      );
      const { unique: entries, skipped: duplicates } =
        removeDuplicateFingerprints(fingerprinted);
      if (duplicates.length > 0) {
        setSkippedFiles([...selection.skipped, ...duplicates]);
      }

      let activeBatch = batch;
      if (!activeBatch) {
        const clientBatchId = crypto.randomUUID();
        const descriptors: CreatePhotoImportFile[] = entries.map(({ file, fingerprint }) => ({
          clientFileId: fingerprint,
          fileName: file.name,
          contentType: file.type,
          sizeBytes: file.size,
        }));
        fileNames = entries.map(({ file }) => file.name);
        activeBatch = await createPhotoImport(tripId, clientBatchId, descriptors);
        saveStoredImport(tripId, { batchId: activeBatch.id, clientBatchId });
        showBatch(activeBatch);
        setBatchId(activeBatch.id);
      }

      // Items are matched to files by fingerprint, so resuming only uploads the files that
      // are still missing, whatever else the user reselects.
      const filesByFingerprint = new Map(
        entries.map(({ file, fingerprint }) => [fingerprint, file]),
      );
      const uploadItems = activeBatch.items
        .filter((item) => item.state === 'AwaitingUpload')
        .flatMap((item) => {
          const file = filesByFingerprint.get(item.clientFileId);
          return file ? [{ item, file }] : [];
        });

      if (uploadItems.length === 0) {
        setError(
          activeBatch.counts.awaitingUpload > 0
            ? 'None of the selected files match the missing uploads.'
            : 'All files in the batch have already been uploaded.',
        );
        return;
      }

      const uploadBatchId = activeBatch.id;
      await runWithConcurrency(uploadItems, uploadConcurrency, ({ item, file }) =>
        uploadPhotoFile(uploadBatchId, item, file),
      );
      await refreshBatch(uploadBatchId);
    } catch (requestError: unknown) {
      setError(importStartErrorMessage(requestError, fileNames));
    } finally {
      setIsUploading(false);
    }
  }

  function startNewImport() {
    clearStoredImport(tripId);
    setBatchId(undefined);
    setAdjustmentMinutes(0);
    setError(undefined);
    setSkippedFiles([]);
    preview.reset();
  }

  return {
    batch,
    isLoading: batchId !== undefined && batchQuery.isPending,
    isUploading,
    error: error ?? queryError,
    skippedFiles,
    adjustmentMinutes,
    setAdjustmentMinutes,
    preview: preview.data,
    isFinalizing: finalize.isPending,
    uploadFiles,
    previewTimes: () => {
      if (batch) {
        preview.mutate(batch.id);
      }
    },
    finalize: () => {
      if (batch) {
        finalize.mutate(batch.id);
      }
    },
    retryItem: (itemId: string) => {
      if (batch) {
        retry.mutate({ id: batch.id, itemId });
      }
    },
    startNewImport,
  };
}
