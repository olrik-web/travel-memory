import { Link, useNavigate } from 'react-router-dom';
import { TripForm } from './TripForm';

export function NewTripPage() {
  const navigate = useNavigate();

  return (
    <main className="page page-narrow">
      <Link className="back-link" to="/trips">
        <span aria-hidden="true">←</span> Alle rejser
      </Link>
      <section className="form-card">
        <p className="eyebrow">Nyt rejseminde</p>
        <h1>Opret en rejse</h1>
        <p className="lede">Start med det vigtigste. Resten af minderne kan komme senere.</p>
        <TripForm onCreated={(tripId) => void navigate(`/trips/${tripId}`)} />
      </section>
    </main>
  );
}
