import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderWithQueryClient } from '../../test/renderWithQueryClient';
import { TripDetailPage } from './TripDetailPage';

const tripId = 'b7eac69b-f395-4284-b77d-dfbccad43f67';
const photoId = '4b1c1f0e-3f7e-4b64-9a2a-5d51f1f0c1aa';

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

// Answers by URL and method, because the trip and its timeline load in parallel.
function stubApi() {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url =
      typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (init?.method === 'DELETE') {
      return new Response(null, { status: 204 });
    }

    if (url.endsWith('/photos')) {
      return jsonResponse({
        items: [
          {
            id: photoId,
            fileName: 'krakow.jpg',
            capturedAtOriginalLocal: '2026-07-12T10:00:00',
            timeAdjustmentMinutes: 0,
            capturedAtTimelineLocal: '2026-07-12T10:00:00',
            exifOffsetMinutes: null,
            thumbnailUrl: 'https://storage.test/thumbnail.jpg',
            webUrl: 'https://storage.test/web.jpg',
            urlsExpireAtUtc: '2026-09-26T12:15:00Z',
            width: 2048,
            height: 1536,
            thumbnailWidth: 480,
            thumbnailHeight: 360,
          },
        ],
      });
    }

    return jsonResponse({
      id: tripId,
      title: 'Poland',
      startDate: '2026-07-11',
      endDate: '2026-07-21',
      createdAtUtc: '2026-09-26T10:00:00Z',
    });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderTrip() {
  renderWithQueryClient(
    <MemoryRouter initialEntries={[`/trips/${tripId}`]}>
      <Routes>
        <Route path="/trips/:tripId" element={<TripDetailPage />} />
        <Route path="/trips" element={<p>Trip list opened</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('photo deletion', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('deletes a photo after confirmation and removes it from the timeline', async () => {
    const user = userEvent.setup();
    const fetchMock = stubApi();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderTrip();

    await user.click(await screen.findByRole('button', { name: 'Delete krakow.jpg' }));

    expect(confirm).toHaveBeenCalledWith('Delete krakow.jpg? This cannot be undone.');
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Delete krakow.jpg' })).not.toBeInTheDocument(),
    );
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/trips/${tripId}/photos/${photoId}`,
      expect.objectContaining({ method: 'DELETE' }),
    );
  });

  it('keeps the photo when the confirmation is cancelled', async () => {
    const user = userEvent.setup();
    const fetchMock = stubApi();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderTrip();

    await user.click(await screen.findByRole('button', { name: 'Delete krakow.jpg' }));

    expect(screen.getByRole('button', { name: 'Delete krakow.jpg' })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ method: 'DELETE' }),
    );
  });
});

describe('trip deletion', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('deletes the trip once its title is typed and returns to the trip list', async () => {
    const user = userEvent.setup();
    const fetchMock = stubApi();
    renderTrip();

    await user.click(await screen.findByRole('button', { name: 'Delete trip' }));
    const confirmButton = screen.getByRole('button', { name: 'Delete permanently' });
    expect(confirmButton).toBeDisabled();
    await user.type(screen.getByLabelText(/to confirm/), 'Poland');
    await user.click(confirmButton);

    expect(await screen.findByText('Trip list opened')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/trips/${tripId}`,
      expect.objectContaining({ method: 'DELETE' }),
    );
  });
});
