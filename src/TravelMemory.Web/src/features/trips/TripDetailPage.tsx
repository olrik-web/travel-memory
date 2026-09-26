import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../../api/http';
import { useDeletePhoto, usePhotoTimeline } from '../photos/photoQueries';
import type { PhotoTimelineItem } from '../photos/types';
import { DeleteTripSection } from './DeleteTripSection';
import { formatTripDates } from './tripDates';
import { useTrip } from './tripQueries';

export function TripDetailPage() {
  const { tripId } = useParams();
  const tripQuery = useTrip(tripId);
  const timelineQuery = usePhotoTimeline(tripId);
  const trip = tripQuery.data;
  const isLoading = tripId !== undefined && tripQuery.isPending;
  const error = tripQuery.error
    ? tripQuery.error instanceof ApiError && tripQuery.error.status === 404
      ? 'The trip was not found.'
      : 'The trip could not be loaded. Try again.'
    : undefined;
  const photos = timelineQuery.data?.items ?? [];
  const deletePhoto = useDeletePhoto(tripId ?? '');
  const photoError = timelineQuery.error
    ? 'The photo timeline could not be loaded.'
    : deletePhoto.error
      ? 'The photo could not be deleted. Try again.'
      : undefined;

  function confirmDelete(photo: PhotoTimelineItem) {
    if (window.confirm(`Delete ${photo.fileName}? This cannot be undone.`)) {
      deletePhoto.mutate(photo.id);
    }
  }

  return (
    <main className="page page-narrow">
      <Link className="back-link" to="/trips">
        <span aria-hidden="true">←</span> All trips
      </Link>

      {isLoading && (
        <div className="status-card" role="status">
          Loading the trip...
        </div>
      )}

      {(error || !tripId) && (
        <div className="status-card status-error" role="alert">
          {error ?? 'The trip id is missing.'}
        </div>
      )}

      {trip && (
        <article className="detail-card">
          <p className="eyebrow">Trip memory</p>
          <h1>{trip.title}</h1>
          <p className="detail-date">{formatTripDates(trip.startDate, trip.endDate)}</p>
          <div className="detail-actions">
            <Link className="button button-primary" to={`/trips/${trip.id}/import`}>
              Import photos
            </Link>
            <Link className="button button-secondary" to={`/trips/${trip.id}/edit`}>
              Edit trip
            </Link>
          </div>
        </article>
      )}

      {photoError && (
        <div className="status-card status-error" role="alert">
          {photoError}
        </div>
      )}

      {trip && photos.length === 0 && !photoError && (
        <div className="detail-placeholder">
          <span aria-hidden="true">✦</span>
          <p>No photos yet. Import JPEG or HEIC photos to start the timeline.</p>
        </div>
      )}

      {photos.length > 0 && (
        <section className="photo-timeline" aria-labelledby="photo-timeline-heading">
          <div className="timeline-heading">
            <p className="eyebrow">Chronological</p>
            <h2 id="photo-timeline-heading">Photo timeline</h2>
          </div>
          <div className="photo-grid">
            {photos.map((photo) => (
              <figure className="photo-card" key={photo.id}>
                <a href={photo.webUrl} target="_blank" rel="noreferrer">
                  <img
                    src={photo.thumbnailUrl}
                    alt={photo.fileName}
                    width={photo.thumbnailWidth}
                    height={photo.thumbnailHeight}
                    loading="lazy"
                  />
                </a>
                <figcaption>
                  <span>{formatPhotoTime(photo.capturedAtTimelineLocal)}</span>
                  <button
                    className="photo-delete"
                    type="button"
                    onClick={() => confirmDelete(photo)}
                    disabled={deletePhoto.isPending}
                    aria-label={`Delete ${photo.fileName}`}
                  >
                    Delete
                  </button>
                </figcaption>
              </figure>
            ))}
          </div>
        </section>
      )}

      {trip && <DeleteTripSection trip={trip} />}
    </main>
  );
}

function formatPhotoTime(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}
