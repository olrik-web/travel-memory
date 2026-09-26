import { Link, useNavigate } from 'react-router-dom';
import { TripForm } from './TripForm';
import { useCreateTrip } from './tripQueries';

export function NewTripPage() {
  const navigate = useNavigate();
  const createTrip = useCreateTrip();

  return (
    <main className="page page-narrow">
      <Link className="back-link" to="/trips">
        <span aria-hidden="true">←</span> All trips
      </Link>
      <section className="form-card">
        <p className="eyebrow">New trip memory</p>
        <h1>Create a trip</h1>
        <p className="lede">Start with the essentials. The rest of the memories can come later.</p>
        <TripForm
          submitLabel="Create trip"
          isSubmitting={createTrip.isPending}
          onSubmit={async (request) => {
            const trip = await createTrip.mutateAsync(request);
            await navigate(`/trips/${trip.id}`);
          }}
        />
      </section>
    </main>
  );
}
