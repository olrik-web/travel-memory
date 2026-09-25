import { screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithQueryClient } from '../../test/renderWithQueryClient';
import { getPhotoImport } from './photoImportApi';
import { PhotoImportPage } from './PhotoImportPage';

vi.mock('./photoImportApi', () => ({
  completePhotoUpload: vi.fn(),
  createPhotoImport: vi.fn(),
  finalizePhotoImport: vi.fn(),
  getPhotoImport: vi.fn(),
  previewPhotoTimes: vi.fn(),
  renewUploadGrant: vi.fn(),
  retryPhotoImportItem: vi.fn(),
}));

describe('photo import resume', () => {
  const tripId = '9541dd1f-814e-46c8-ad1d-e29798bf9a19';
  const batchId = 'c1205d86-ed13-4ae7-a319-425c8e24bb6b';

  beforeEach(() => {
    localStorage.clear();
    vi.resetAllMocks();
  });

  it('loads the stored batch and asks for only the missing files', async () => {
    localStorage.setItem(
      `travel-memory:photo-import:${tripId}`,
      JSON.stringify({ batchId, clientBatchId: crypto.randomUUID() }),
    );
    vi.mocked(getPhotoImport).mockResolvedValue({
      id: batchId,
      tripId,
      state: 'Uploading',
      counts: {
        total: 2,
        awaitingUpload: 1,
        analyzing: 0,
        ready: 1,
        processing: 0,
        succeeded: 0,
        duplicates: 0,
        failed: 0,
      },
      items: [
        {
          id: '40bf32dd-6907-4360-8c0a-a27843220faf',
          clientFileId: 'a'.repeat(64),
          fileName: 'missing.heic',
          contentType: 'image/heic',
          sizeBytes: 2048,
          state: 'AwaitingUpload',
          outcome: 'None',
          canRetry: false,
        },
        {
          id: 'f21fd90c-139c-41f5-b09b-5542223072a8',
          clientFileId: 'b'.repeat(64),
          fileName: 'ready.jpg',
          contentType: 'image/jpeg',
          sizeBytes: 1024,
          state: 'ReadyForReview',
          outcome: 'None',
          canRetry: false,
        },
      ],
    });

    renderWithQueryClient(
      <MemoryRouter initialEntries={[`/trips/${tripId}/import`]}>
        <Routes>
          <Route path="/trips/:tripId/import" element={<PhotoImportPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { name: 'Resume upload' }))
      .toBeInTheDocument();
    expect(screen.getByText(/1 file missing/)).toBeInTheDocument();
    expect(screen.getByText('missing.heic')).toBeInTheDocument();
    expect(getPhotoImport).toHaveBeenCalledWith(batchId, expect.any(AbortSignal));
  });

  it('forgets a stored import that can no longer be loaded', async () => {
    const key = `travel-memory:photo-import:${tripId}`;
    localStorage.setItem(key, JSON.stringify({ batchId, clientBatchId: crypto.randomUUID() }));
    vi.mocked(getPhotoImport).mockRejectedValue(new Error('Not found'));

    renderWithQueryClient(
      <MemoryRouter initialEntries={[`/trips/${tripId}/import`]}>
        <Routes>
          <Route path="/trips/:tripId/import" element={<PhotoImportPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(
      await screen.findByText('The previous import could not be loaded. Start a new import.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Select photos' })).toBeInTheDocument();
    expect(localStorage.getItem(key)).toBeNull();
    expect(getPhotoImport).toHaveBeenCalledTimes(1);
  });
});
