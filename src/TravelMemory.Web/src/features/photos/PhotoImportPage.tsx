import { type ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../../api/http';
import { runWithConcurrency } from './boundedConcurrency';
import { createFileFingerprint } from './fileFingerprint';
import {
  completePhotoUpload,
  createPhotoImport,
  finalizePhotoImport,
  getPhotoImport,
  previewPhotoTimes,
  renewUploadGrant,
  retryPhotoImportItem,
} from './photoImportApi';
import type {
  CreatePhotoImportFile,
  PhotoImportBatch,
  PhotoImportItem,
  PhotoTimePreview,
} from './types';

const uploadConcurrency = 4;
const terminalStates = new Set(['Completed', 'CompletedWithErrors']);

interface StoredImport {
  batchId: string;
  clientBatchId: string;
}

function storageKey(tripId: string) {
  return `travel-memory:photo-import:${tripId}`;
}

function readStoredImport(tripId: string): StoredImport | undefined {
  const value = localStorage.getItem(storageKey(tripId));
  if (!value) {
    return undefined;
  }

  try {
    return JSON.parse(value) as StoredImport;
  } catch {
    localStorage.removeItem(storageKey(tripId));
    return undefined;
  }
}

function describeState(item: PhotoImportItem) {
  const descriptions: Record<string, string> = {
    AwaitingUpload: 'Waiting for upload',
    QueuedForAnalysis: 'Waiting for analysis',
    Analyzing: 'Reading EXIF and checking for duplicates',
    ReadyForReview: 'Ready for time preview',
    QueuedForProcessing: 'Waiting for processing',
    Processing: 'Creating web copy and thumbnail',
    CleanupPending: 'Verified - cleaning up the original',
    Succeeded: item.outcome === 'Duplicate' ? 'Duplicate - not imported again' : 'Imported',
    Failed: 'Could not be processed',
    CleanupFailed: 'Imported, but cleanup needs attention',
  };
  return descriptions[item.state] ?? item.state;
}

function formatFileCount(count: number) {
  return count === 1 ? '1 file' : `${count} files`;
}

function formatLocalDateTime(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function PhotoImportPage() {
  const { tripId } = useParams();
  const storedImport = useMemo(
    () => (tripId ? readStoredImport(tripId) : undefined),
    [tripId],
  );
  const [batch, setBatch] = useState<PhotoImportBatch>();
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string>();
  const [adjustmentMinutes, setAdjustmentMinutes] = useState(0);
  const [preview, setPreview] = useState<PhotoTimePreview>();
  const [isFinalizing, setIsFinalizing] = useState(false);

  const refreshBatch = useCallback(
    async (batchId: string, signal?: AbortSignal) => {
      const nextBatch = await getPhotoImport(batchId, signal);
      setBatch(nextBatch);
      return nextBatch;
    },
    [],
  );

  useEffect(() => {
    if (!tripId) {
      return;
    }

    if (!storedImport) {
      return;
    }

    const controller = new AbortController();
    void getPhotoImport(storedImport.batchId, controller.signal)
      .then(setBatch)
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return;
        }

        localStorage.removeItem(storageKey(tripId));
        setError('The previous import could not be loaded. Start a new import.');
      });

    return () => controller.abort();
  }, [refreshBatch, storedImport, tripId]);

  useEffect(() => {
    if (!batch || terminalStates.has(batch.state)) {
      return;
    }

    const controller = new AbortController();
    const timer = window.setInterval(() => {
      void refreshBatch(batch.id, controller.signal).catch(
        (requestError: unknown) => {
          if (!(requestError instanceof DOMException && requestError.name === 'AbortError')) {
            setError('The status could not be updated. Retrying.');
          }
        },
      );
    }, 1500);

    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [batch, refreshBatch]);

  const awaitingUpload = useMemo(
    () => batch?.items.filter((item) => item.state === 'AwaitingUpload') ?? [],
    [batch],
  );
  const isLoading = Boolean(storedImport && !batch && !error);

  async function handleFilesSelected(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    event.target.value = '';
    if (!tripId || files.length === 0) {
      return;
    }

    if (files.length > 500) {
      setError('Select at most 500 photos at a time.');
      return;
    }

    setError(undefined);
    setIsUploading(true);

    try {
      const entries = await Promise.all(
        files.map(async (file) => ({
          file,
          fingerprint: await createFileFingerprint(file),
        })),
      );

      let activeBatch = batch;
      if (!activeBatch) {
        const activeClientBatchId = crypto.randomUUID();
        const descriptors: CreatePhotoImportFile[] = entries.map(
          ({ file, fingerprint }) => ({
            clientFileId: fingerprint,
            fileName: file.name,
            contentType: file.type,
            sizeBytes: file.size,
          }),
        );
        activeBatch = await createPhotoImport(
          tripId,
          activeClientBatchId,
          descriptors,
        );
        localStorage.setItem(
          storageKey(tripId),
          JSON.stringify({
            batchId: activeBatch.id,
            clientBatchId: activeClientBatchId,
          } satisfies StoredImport),
        );
        setBatch(activeBatch);
      }

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

      await runWithConcurrency(
        uploadItems,
        uploadConcurrency,
        async ({ item, file }) => {
          await uploadFile(activeBatch.id, item, file);
        },
      );
      await refreshBatch(activeBatch.id);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof ApiError
          ? requestError.message
          : requestError instanceof Error
            ? requestError.message
            : 'The photo import could not be started.',
      );
    } finally {
      setIsUploading(false);
    }
  }

  async function handlePreview() {
    if (!batch) {
      return;
    }

    setError(undefined);
    try {
      setPreview(await previewPhotoTimes(batch.id, adjustmentMinutes));
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The time preview could not be loaded.',
      );
    }
  }

  async function handleFinalize() {
    if (!batch) {
      return;
    }

    setIsFinalizing(true);
    setError(undefined);
    try {
      const nextBatch = await finalizePhotoImport(batch.id, adjustmentMinutes);
      setBatch(nextBatch);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The import could not be finalized.',
      );
    } finally {
      setIsFinalizing(false);
    }
  }

  async function handleRetry(itemId: string) {
    if (!batch) {
      return;
    }

    setError(undefined);
    try {
      await retryPhotoImportItem(batch.id, itemId);
      await refreshBatch(batch.id);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The file could not be retried.',
      );
    }
  }

  function startNewBatch() {
    if (tripId) {
      localStorage.removeItem(storageKey(tripId));
    }

    setBatch(undefined);
    setPreview(undefined);
    setAdjustmentMinutes(0);
    setError(undefined);
  }

  if (!tripId) {
    return <main className="page">The trip id is missing.</main>;
  }

  return (
    <main className="page page-import">
      <Link className="back-link" to={`/trips/${tripId}`}>
        <span aria-hidden="true">←</span> Back to the trip
      </Link>
      <div className="page-heading">
        <div>
          <p className="eyebrow">Photo import</p>
          <h1>Add trip memories</h1>
          <p className="lede">
            JPEG and HEIC photos are uploaded directly to private temporary storage.
          </p>
        </div>
      </div>

      {error && (
        <div className="status-card status-error" role="alert">
          {error}
        </div>
      )}

      {isLoading && (
        <div className="status-card" role="status">
          Loading import status...
        </div>
      )}

      {!isLoading && (!batch || awaitingUpload.length > 0) && (
        <section className="import-panel">
          <h2>{batch ? 'Resume upload' : 'Select photos'}</h2>
          <p>
            {batch
              ? `${formatFileCount(awaitingUpload.length)} missing. Select them again; files already uploaded are skipped.`
              : 'Select up to 500 JPEG or HEIC files. Four files are uploaded at a time.'}
          </p>
          <label className="button button-primary file-picker">
            {isUploading ? 'Uploading...' : batch ? 'Select missing files' : 'Select photos'}
            <input
              type="file"
              accept=".jpg,.jpeg,.heic,.heif,image/jpeg,image/heic,image/heif"
              multiple
              onChange={(event) => void handleFilesSelected(event)}
              disabled={isUploading}
            />
          </label>
        </section>
      )}

      {batch && (
        <>
          <section className="import-summary" aria-label="Import status">
            <strong>{formatFileCount(batch.counts.total)}</strong>
            <span>{batch.counts.awaitingUpload} awaiting upload</span>
            <span>{batch.counts.analyzing} analyzing</span>
            <span>{batch.counts.ready} ready</span>
            <span>{batch.counts.processing} processing</span>
            <span>{batch.counts.succeeded} imported</span>
            <span>{batch.counts.duplicates} duplicates</span>
            <span>{batch.counts.failed} failed</span>
          </section>

          {batch.state === 'ReadyForReview' && (
            <section className="import-panel time-review">
              <div>
                <p className="eyebrow">Timeline</p>
                <h2>Check the camera time</h2>
                <p>
                  The adjustment is stored separately. The photo's EXIF time and original file are not changed.
                </p>
              </div>
              <div className="offset-controls">
                <label htmlFor="adjustmentMinutes">Time adjustment in minutes</label>
                <input
                  id="adjustmentMinutes"
                  type="number"
                  min="-1440"
                  max="1440"
                  step="15"
                  value={adjustmentMinutes}
                  onChange={(event) => setAdjustmentMinutes(event.target.valueAsNumber || 0)}
                />
                <small>Example: -60 moves the camera time back one hour.</small>
              </div>
              <div className="form-actions import-actions">
                <button className="button button-secondary" type="button" onClick={() => void handlePreview()}>
                  Show preview
                </button>
                <button
                  className="button button-primary"
                  type="button"
                  onClick={() => void handleFinalize()}
                  disabled={!preview || isFinalizing}
                >
                  {isFinalizing ? 'Starting processing...' : 'Approve and process'}
                </button>
              </div>
              {preview && (
                <div className="time-preview">
                  {preview.items.slice(0, 12).map((item) => (
                    <div key={item.itemId}>
                      <strong>{item.fileName}</strong>
                      <span>
                        {formatLocalDateTime(item.capturedAtOriginalLocal)} →
                        {' '}
                        {formatLocalDateTime(item.capturedAtTimelineLocal)}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </section>
          )}

          <ul className="import-items" aria-label="File status">
            {batch.items.map((item) => (
              <li key={item.id}>
                <div>
                  <strong>{item.fileName}</strong>
                  <span>{describeState(item)}</span>
                  {item.errorMessage && <p className="field-error">{item.errorMessage}</p>}
                  {item.originalRetainedUntilUtc && (
                    <p className="retention-note">
                      Temporary original kept until{' '}
                      {formatLocalDateTime(item.originalRetainedUntilUtc)}.
                    </p>
                  )}
                </div>
                {item.canRetry && (
                  <button
                    className="button button-secondary"
                    type="button"
                    onClick={() => void handleRetry(item.id)}
                  >
                    Try again
                  </button>
                )}
              </li>
            ))}
          </ul>

          {terminalStates.has(batch.state) && (
            <div className="import-complete">
              <Link className="button button-primary" to={`/trips/${tripId}`}>
                View the photo timeline
              </Link>
              <button className="button button-secondary" type="button" onClick={startNewBatch}>
                Start a new import
              </button>
            </div>
          )}
        </>
      )}
    </main>
  );
}

async function uploadFile(batchId: string, item: PhotoImportItem, file: File) {
  let uploadUrl = item.uploadUrl;
  if (!uploadUrl || !item.uploadExpiresAtUtc || new Date(item.uploadExpiresAtUtc) <= new Date()) {
    uploadUrl = (await renewUploadGrant(batchId, item.id)).uploadUrl;
  }

  let response = await putBlob(uploadUrl, item.contentType, file);
  if (response.status === 403) {
    const renewed = await renewUploadGrant(batchId, item.id);
    response = await putBlob(renewed.uploadUrl, item.contentType, file);
  }

  if (!response.ok) {
    throw new Error(
      `${item.fileName}: upload failed with status ${response.status}. Check your connection and select the file again.`,
    );
  }

  await completePhotoUpload(batchId, item.id);
}

function putBlob(uploadUrl: string, contentType: string, file: File) {
  return fetch(uploadUrl, {
    method: 'PUT',
    headers: {
      'x-ms-blob-type': 'BlockBlob',
      'Content-Type': contentType,
    },
    body: file,
  });
}
