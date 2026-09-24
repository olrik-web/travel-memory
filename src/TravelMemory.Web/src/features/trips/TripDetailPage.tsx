import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../../api/http';
import { getPhotoTimeline } from '../photos/photoImportApi';
import type { PhotoTimelineItem } from '../photos/types';
import { getTrip } from './tripApi';
import { formatTripDates } from './tripDates';
import type { Trip } from './types';

export function TripDetailPage() {
  const { tripId } = useParams();
  const [trip, setTrip] = useState<Trip>();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [photos, setPhotos] = useState<PhotoTimelineItem[]>([]);
  const [photoError, setPhotoError] = useState<string>();

  useEffect(() => {
    if (!tripId) {
      return;
    }

    const controller = new AbortController();

    void getTrip(tripId, controller.signal)
      .then(setTrip)
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return;
        }

        setError(
          requestError instanceof ApiError && requestError.status === 404
            ? 'Rejsen blev ikke fundet.'
            : 'Rejsen kunne ikke hentes. Prøv igen.',
        );
      })
      .finally(() => setIsLoading(false));

    return () => controller.abort();
  }, [tripId]);

  useEffect(() => {
    if (!tripId) {
      return;
    }

    const controller = new AbortController();
    void getPhotoTimeline(tripId, controller.signal)
      .then((timeline) => setPhotos(timeline.items))
      .catch((requestError: unknown) => {
        if (!(requestError instanceof DOMException && requestError.name === 'AbortError')) {
          setPhotoError('Fototidslinjen kunne ikke hentes.');
        }
      });

    return () => controller.abort();
  }, [tripId]);

  return (
    <main className="page page-narrow">
      <Link className="back-link" to="/trips">
        <span aria-hidden="true">←</span> Alle rejser
      </Link>

      {isLoading && (
        <div className="status-card" role="status">
          Henter rejsen...
        </div>
      )}

      {(error || !tripId) && (
        <div className="status-card status-error" role="alert">
          {error ?? 'Rejsens id mangler.'}
        </div>
      )}

      {trip && (
        <article className="detail-card">
          <p className="eyebrow">Rejseminde</p>
          <h1>{trip.title}</h1>
          <p className="detail-date">{formatTripDates(trip.startDate, trip.endDate)}</p>
          <div className="detail-actions">
            <Link className="button button-primary" to={`/trips/${trip.id}/import`}>
              Importér fotos
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
          <p>Ingen fotos endnu. Importér JPEG eller HEIC for at starte tidslinjen.</p>
        </div>
      )}

      {photos.length > 0 && (
        <section className="photo-timeline" aria-labelledby="photo-timeline-heading">
          <div className="timeline-heading">
            <p className="eyebrow">Kronologisk</p>
            <h2 id="photo-timeline-heading">Fototidslinje</h2>
          </div>
          <div className="photo-grid">
            {photos.map((photo) => (
              <a
                className="photo-card"
                href={photo.webUrl}
                key={photo.id}
                target="_blank"
                rel="noreferrer"
              >
                <img
                  src={photo.thumbnailUrl}
                  alt={photo.fileName}
                  width={photo.thumbnailWidth}
                  height={photo.thumbnailHeight}
                  loading="lazy"
                />
                <span>{formatPhotoTime(photo.capturedAtTimelineLocal)}</span>
              </a>
            ))}
          </div>
        </section>
      )}
    </main>
  );
}

function formatPhotoTime(value: string) {
  return new Intl.DateTimeFormat('da-DK', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}
