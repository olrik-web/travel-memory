import { Link, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/http';
import { TripForm } from './TripForm';
import { useTrip, useUpdateTrip } from './tripQueries';

export function EditTripPage() {
  const { tripId } = useParams();

  if (!tripId) {
    return <main className="page page-narrow">The trip id is missing.</main>;
  }

  return <EditTrip tripId={tripId} />;
}

function EditTrip({ tripId }: { tripId: string }) {
  const navigate = useNavigate();
  const tripQuery = useTrip(tripId);
  const updateTrip = useUpdateTrip(tripId);
  const trip = tripQuery.data;
  const error = tripQuery.error
    ? tripQuery.error instanceof ApiError && tripQuery.error.status === 404
      ? 'The trip was not found.'
      : 'The trip could not be loaded. Try again.'
    : undefined;

  return (
    <main className="page page-narrow">
      <Link className="back-link" to={`/trips/${tripId}`}>
        <span aria-hidden="true">←</span> Back to the trip
      </Link>

      {tripQuery.isPending && (
        <div className="status-card" role="status">
          Loading the trip...
        </div>
      )}

      {error && (
        <div className="status-card status-error" role="alert">
          {error}
        </div>
      )}

      {trip && (
        <section className="form-card">
          <p className="eyebrow">Trip memory</p>
          <h1>Edit trip</h1>
          <TripForm
            initialValues={{
              title: trip.title,
              startDate: trip.startDate ?? '',
              endDate: trip.endDate ?? '',
            }}
            submitLabel="Save changes"
            isSubmitting={updateTrip.isPending}
            onSubmit={async (request) => {
              await updateTrip.mutateAsync(request);
              await navigate(`/trips/${tripId}`);
            }}
          />
        </section>
      )}
    </main>
  );
}
