import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderWithQueryClient } from '../../test/renderWithQueryClient';
import { EditTripPage } from './EditTripPage';

const tripId = 'b7eac69b-f395-4284-b77d-dfbccad43f67';
const trip = {
  id: tripId,
  title: 'Poland',
  startDate: null,
  endDate: null,
  createdAtUtc: '2026-09-26T10:00:00Z',
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('edit trip flow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('prefills the trip, saves the changes, and returns to the trip', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValueOnce(jsonResponse(trip));
    vi.stubGlobal('fetch', fetchMock);

    renderWithQueryClient(
      <MemoryRouter initialEntries={[`/trips/${tripId}/edit`]}>
        <Routes>
          <Route path="/trips/:tripId/edit" element={<EditTripPage />} />
          <Route path="/trips/:tripId" element={<p>Detail route opened</p>} />
        </Routes>
      </MemoryRouter>,
    );

    const title = await screen.findByLabelText('Title');
    expect(title).toHaveValue('Poland');
    await user.clear(title);
    await user.type(title, 'Summer in Poland');
    fetchMock.mockResolvedValueOnce(jsonResponse({ ...trip, title: 'Summer in Poland' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText('Detail route opened')).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[1]!;
    expect(url).toBe(`/api/trips/${tripId}`);
    expect(init?.method).toBe('PUT');
    expect(JSON.parse(init?.body as string)).toEqual({
      title: 'Summer in Poland',
      startDate: null,
      endDate: null,
    });
  });

  it('shows the API\'s field errors next to the fields', async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(jsonResponse(trip))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            title: 'One or more validation errors occurred.',
            errors: { title: ['The title can be at most 200 characters.'] },
          }),
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      );
    vi.stubGlobal('fetch', fetchMock);

    renderWithQueryClient(
      <MemoryRouter initialEntries={[`/trips/${tripId}/edit`]}>
        <Routes>
          <Route path="/trips/:tripId/edit" element={<EditTripPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByLabelText('Title');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText('The title can be at most 200 characters.')).toBeInTheDocument();
  });
});
