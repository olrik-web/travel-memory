import { describe, expect, it } from 'vitest';
import {
  describeRejectedFiles,
  describeSkippedFiles,
  maximumPhotoSizeBytes,
  partitionSelection,
  removeDuplicateFingerprints,
} from './photoSelection';

const file = (name: string, type: string, size = 1024) => ({ name, type, size });

describe('partitionSelection', () => {
  it('keeps JPEG and HEIC photos, including HEIC files without a browser type', () => {
    const photos = [
      file('beach.jpg', 'image/jpeg'),
      file('dinner.JPEG', 'image/jpeg'),
      file('krakow.heic', ''),
      file('tatra.heif', 'image/heif'),
    ];

    expect(partitionSelection(photos)).toEqual({ accepted: photos, skipped: [] });
  });

  it('skips videos, other files, empty files, and files over 100 MB', () => {
    const { accepted, skipped } = partitionSelection([
      file('beach.jpg', 'image/jpeg'),
      file('boat.mov', 'video/quicktime'),
      file('notes.txt', 'text/plain'),
      file('renamed.jpg', 'video/mp4'),
      file('empty.jpg', 'image/jpeg', 0),
      file('huge.jpg', 'image/jpeg', maximumPhotoSizeBytes + 1),
    ]);

    expect(accepted.map((photo) => photo.name)).toEqual(['beach.jpg']);
    expect(skipped).toEqual([
      { name: 'boat.mov', reason: 'unsupported' },
      { name: 'notes.txt', reason: 'unsupported' },
      { name: 'renamed.jpg', reason: 'unsupported' },
      { name: 'empty.jpg', reason: 'empty' },
      { name: 'huge.jpg', reason: 'tooLarge' },
    ]);
  });
});

describe('removeDuplicateFingerprints', () => {
  it('keeps the first of two files with the same identity', () => {
    const first = { file: file('a.jpg', 'image/jpeg'), fingerprint: 'same' };
    const second = { file: file('a.jpg', 'image/jpeg'), fingerprint: 'same' };

    expect(removeDuplicateFingerprints([first, second])).toEqual({
      unique: [first],
      skipped: [{ name: 'a.jpg', reason: 'duplicate' }],
    });
  });
});

describe('describeSkippedFiles', () => {
  it('summarizes the skipped files by reason', () => {
    expect(
      describeSkippedFiles([
        { name: 'a.mov', reason: 'unsupported' },
        { name: 'b.mp4', reason: 'unsupported' },
        { name: 'c.jpg', reason: 'tooLarge' },
      ]),
    ).toBe('3 files were skipped: 2 not JPEG or HEIC, 1 larger than 100 MB.');
    expect(describeSkippedFiles([{ name: 'a.mov', reason: 'unsupported' }])).toBe(
      '1 file was skipped: 1 not JPEG or HEIC.',
    );
    expect(describeSkippedFiles([])).toBeUndefined();
  });
});

describe('describeRejectedFiles', () => {
  it('names the rejected files instead of their positions in the request', () => {
    expect(
      describeRejectedFiles(
        {
          'files[1]': ['Only JPEG and HEIC are supported.'],
          'files[0].sizeBytes': ['The file must be at most 100 MB.'],
          files: ['The same file identity appears more than once in the batch.'],
        },
        ['big.jpg', 'clip.mov'],
      ),
    ).toEqual([
      'clip.mov: Only JPEG and HEIC are supported.',
      'big.jpg: The file must be at most 100 MB.',
      'The same file identity appears more than once in the batch.',
    ]);
  });
});
