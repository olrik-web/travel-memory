import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/http';
import { listTrips } from './tripApi';
import { formatTripDates } from './tripDates';
import type { Trip } from './types';

export function TripListPage() {
  const [trips, setTrips] = useState<Trip[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const controller = new AbortController();

    void listTrips(controller.signal)
      .then((response) => setTrips(response.items))
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return;
        }

        setError(
          requestError instanceof ApiError
            ? requestError.message
            : 'Your trips could not be loaded. Try again.',
        );
      })
      .finally(() => setIsLoading(false));

    return () => controller.abort();
  }, []);

  return (
    <main className="page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Trip archive</p>
          <h1>Your trips</h1>
          <p className="lede">A calm place for the experiences you want to remember.</p>
        </div>
        <Link className="button button-primary" to="/trips/new">
          New trip
        </Link>
      </div>

      {isLoading && (
        <div className="status-card" role="status">
          Loading your trips...
        </div>
      )}

      {error && (
        <div className="status-card status-error" role="alert">
          {error}
        </div>
      )}

      {!isLoading && !error && trips.length === 0 && (
        <section className="empty-state">
          <span className="empty-icon" aria-hidden="true">
            ✦
          </span>
          <h2>Your trip archive is ready</h2>
          <p>Create the first trip you want to keep as a memory.</p>
          <Link className="button button-primary" to="/trips/new">
            Create first trip
          </Link>
        </section>
      )}

      {!isLoading && !error && trips.length > 0 && (
        <ul className="trip-grid" aria-label="Trips">
          {trips.map((trip) => (
            <li key={trip.id}>
              <Link className="trip-card" to={`/trips/${trip.id}`}>
                <span className="trip-card-accent" aria-hidden="true" />
                <span className="trip-card-content">
                  <span className="trip-date">
                    {formatTripDates(trip.startDate, trip.endDate)}
                  </span>
                  <strong>{trip.title}</strong>
                  <span className="trip-open">
                    Open trip <span aria-hidden="true">→</span>
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
