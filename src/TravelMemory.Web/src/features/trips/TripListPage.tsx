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
            : 'Rejserne kunne ikke hentes. Prøv igen.',
        );
      })
      .finally(() => setIsLoading(false));

    return () => controller.abort();
  }, []);

  return (
    <main className="page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Rejsearkiv</p>
          <h1>Dine rejser</h1>
          <p className="lede">Et roligt sted til de oplevelser, du vil huske.</p>
        </div>
        <Link className="button button-primary" to="/trips/new">
          Ny rejse
        </Link>
      </div>

      {isLoading && (
        <div className="status-card" role="status">
          Henter dine rejser...
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
          <h2>Dit rejsearkiv er klar</h2>
          <p>Opret den første rejse, du gerne vil gemme som et minde.</p>
          <Link className="button button-primary" to="/trips/new">
            Opret første rejse
          </Link>
        </section>
      )}

      {!isLoading && !error && trips.length > 0 && (
        <ul className="trip-grid" aria-label="Rejser">
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
                    Åbn rejse <span aria-hidden="true">→</span>
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
