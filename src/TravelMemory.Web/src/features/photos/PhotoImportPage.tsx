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
    AwaitingUpload: 'Venter på upload',
    QueuedForAnalysis: 'Venter på analyse',
    Analyzing: 'Læser EXIF og kontrollerer dublet',
    ReadyForReview: 'Klar til tids-preview',
    QueuedForProcessing: 'Venter på behandling',
    Processing: 'Opretter webkopi og thumbnail',
    CleanupPending: 'Verificeret - rydder originalen op',
    Succeeded: item.outcome === 'Duplicate' ? 'Dublet - ikke importeret igen' : 'Importeret',
    Failed: 'Kunne ikke behandles',
    CleanupFailed: 'Importeret, men oprydning kræver handling',
  };
  return descriptions[item.state] ?? item.state;
}

function formatLocalDateTime(value: string) {
  return new Intl.DateTimeFormat('da-DK', {
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
        setError('Den tidligere import kunne ikke hentes. Start en ny import.');
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
            setError('Status kunne ikke opdateres. Forbindelsen forsøges igen.');
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
      setError('Vælg højst 500 fotos ad gangen.');
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
            ? 'Ingen af de valgte filer matcher de uploads, der mangler.'
            : 'Alle filer i batchen er allerede uploadet.',
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
            : 'Fotoimporten kunne ikke startes.',
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
          : 'Tids-preview kunne ikke hentes.',
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
          : 'Importen kunne ikke finaliseres.',
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
          : 'Filen kunne ikke genbehandles.',
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
    return <main className="page">Rejsens id mangler.</main>;
  }

  return (
    <main className="page page-import">
      <Link className="back-link" to={`/trips/${tripId}`}>
        <span aria-hidden="true">←</span> Tilbage til rejsen
      </Link>
      <div className="page-heading">
        <div>
          <p className="eyebrow">Fotoimport</p>
          <h1>Tilføj rejseminder</h1>
          <p className="lede">
            JPEG og HEIC uploades direkte til et privat midlertidigt lager.
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
          Henter importstatus...
        </div>
      )}

      {!isLoading && (!batch || awaitingUpload.length > 0) && (
        <section className="import-panel">
          <h2>{batch ? 'Genoptag upload' : 'Vælg fotos'}</h2>
          <p>
            {batch
              ? `${awaitingUpload.length} filer mangler. Vælg dem igen; allerede uploadede filer springes over.`
              : 'Vælg op til 500 JPEG- eller HEIC-filer. Fire filer uploades ad gangen.'}
          </p>
          <label className="button button-primary file-picker">
            {isUploading ? 'Uploader...' : batch ? 'Vælg manglende filer' : 'Vælg fotos'}
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
          <section className="import-summary" aria-label="Importstatus">
            <strong>{batch.counts.total} filer</strong>
            <span>{batch.counts.awaitingUpload} venter på upload</span>
            <span>{batch.counts.analyzing} analyseres</span>
            <span>{batch.counts.ready} klar</span>
            <span>{batch.counts.processing} behandles</span>
            <span>{batch.counts.succeeded} importeret</span>
            <span>{batch.counts.duplicates} dubletter</span>
            <span>{batch.counts.failed} fejl</span>
          </section>

          {batch.state === 'ReadyForReview' && (
            <section className="import-panel time-review">
              <div>
                <p className="eyebrow">Tidslinje</p>
                <h2>Kontrollér kameraets tid</h2>
                <p>
                  Forskydningen gemmes separat. Fotoets EXIF-tid og originalfil ændres ikke.
                </p>
              </div>
              <div className="offset-controls">
                <label htmlFor="adjustmentMinutes">Tidsforskydning i minutter</label>
                <input
                  id="adjustmentMinutes"
                  type="number"
                  min="-1440"
                  max="1440"
                  step="15"
                  value={adjustmentMinutes}
                  onChange={(event) => setAdjustmentMinutes(event.target.valueAsNumber || 0)}
                />
                <small>Eksempel: -60 flytter kameraets tid én time tilbage.</small>
              </div>
              <div className="form-actions import-actions">
                <button className="button button-secondary" type="button" onClick={() => void handlePreview()}>
                  Vis preview
                </button>
                <button
                  className="button button-primary"
                  type="button"
                  onClick={() => void handleFinalize()}
                  disabled={!preview || isFinalizing}
                >
                  {isFinalizing ? 'Starter behandling...' : 'Godkend og behandl'}
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

          <ul className="import-items" aria-label="Filstatus">
            {batch.items.map((item) => (
              <li key={item.id}>
                <div>
                  <strong>{item.fileName}</strong>
                  <span>{describeState(item)}</span>
                  {item.errorMessage && <p className="field-error">{item.errorMessage}</p>}
                  {item.originalRetainedUntilUtc && (
                    <p className="retention-note">
                      Midlertidig original beholdes til{' '}
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
                    Prøv igen
                  </button>
                )}
              </li>
            ))}
          </ul>

          {terminalStates.has(batch.state) && (
            <div className="import-complete">
              <Link className="button button-primary" to={`/trips/${tripId}`}>
                Se fototidslinjen
              </Link>
              <button className="button button-secondary" type="button" onClick={startNewBatch}>
                Start ny import
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
      `${item.fileName}: upload fejlede med status ${response.status}. Kontrollér forbindelsen og vælg filen igen.`,
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
