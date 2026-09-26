import type { ChangeEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { describeSkippedFiles } from './photoSelection';
import {
  describeItemState,
  formatFileCount,
  formatLocalDateTime,
  isTerminalBatch,
} from './photoImportStatus';
import { usePhotoImport } from './usePhotoImport';

export function PhotoImportPage() {
  const { tripId } = useParams();

  if (!tripId) {
    return <main className="page">The trip id is missing.</main>;
  }

  // Keyed by trip so navigating between trips starts from a fresh import state.
  return <PhotoImport key={tripId} tripId={tripId} />;
}

function PhotoImport({ tripId }: { tripId: string }) {
  const {
    batch,
    isLoading,
    isUploading,
    error,
    skippedFiles,
    adjustmentMinutes,
    setAdjustmentMinutes,
    preview,
    isFinalizing,
    uploadFiles,
    previewTimes,
    finalize,
    retryItem,
    startNewImport,
  } = usePhotoImport(tripId);
  const awaitingUpload =
    batch?.items.filter((item) => item.state === 'AwaitingUpload') ?? [];

  async function handleFilesSelected(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    // Clear the input so selecting the same files again (to resume) fires a change event.
    event.target.value = '';
    await uploadFiles(files);
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

      {skippedFiles.length > 0 && (
        <div className="status-card" role="status">
          <p>{describeSkippedFiles(skippedFiles)}</p>
          <details>
            <summary>Show skipped files</summary>
            <ul className="skipped-files">
              {skippedFiles.map((file, index) => (
                <li key={`${index}-${file.name}`}>{file.name}</li>
              ))}
            </ul>
          </details>
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
              : 'Select up to 500 JPEG or HEIC photos. Other files, such as videos, are skipped. Four files are uploaded at a time.'}
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
                <button className="button button-secondary" type="button" onClick={previewTimes}>
                  Show preview
                </button>
                <button
                  className="button button-primary"
                  type="button"
                  onClick={finalize}
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
                  <span>{describeItemState(item)}</span>
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
                    onClick={() => retryItem(item.id)}
                  >
                    Try again
                  </button>
                )}
              </li>
            ))}
          </ul>

          {isTerminalBatch(batch) && (
            <div className="import-complete">
              <Link className="button button-primary" to={`/trips/${tripId}`}>
                View the photo timeline
              </Link>
              <button className="button button-secondary" type="button" onClick={startNewImport}>
                Start a new import
              </button>
            </div>
          )}
        </>
      )}
    </main>
  );
}
