// Mirrors the API's rules for a photo import (SupportedPhotoMedia and the batch
// validation in CreatePhotoImport), so a selection that also contains other files, such as
// videos from a phone folder, imports its photos instead of being rejected as a whole.
export const maximumPhotoSizeBytes = 100 * 1024 * 1024;
const maximumFileNameLength = 255;

// Browsers often report an empty type for HEIC files, which the API accepts too.
const allowedContentTypes: Record<string, readonly string[]> = {
  '.jpg': ['', 'image/jpeg'],
  '.jpeg': ['', 'image/jpeg'],
  '.heic': ['', 'image/heic', 'image/heif'],
  '.heif': ['', 'image/heic', 'image/heif'],
};

export type SkipReason = 'unsupported' | 'empty' | 'tooLarge' | 'duplicate';

export interface SelectedFile {
  name: string;
  size: number;
  type: string;
}

export interface SkippedFile {
  name: string;
  reason: SkipReason;
}

export function partitionSelection<T extends SelectedFile>(files: readonly T[]) {
  const accepted: T[] = [];
  const skipped: SkippedFile[] = [];
  for (const file of files) {
    const reason = skipReason(file);
    if (reason) {
      skipped.push({ name: file.name, reason });
    } else {
      accepted.push(file);
    }
  }

  return { accepted, skipped };
}

// The API rejects a batch that contains the same file identity twice, for example when
// the same file is picked from two folders.
export function removeDuplicateFingerprints<T extends { file: SelectedFile; fingerprint: string }>(
  entries: readonly T[],
) {
  const seen = new Set<string>();
  const unique: T[] = [];
  const skipped: SkippedFile[] = [];
  for (const entry of entries) {
    if (seen.has(entry.fingerprint)) {
      skipped.push({ name: entry.file.name, reason: 'duplicate' });
    } else {
      seen.add(entry.fingerprint);
      unique.push(entry);
    }
  }

  return { unique, skipped };
}

const reasonDescriptions: Record<SkipReason, string> = {
  unsupported: 'not JPEG or HEIC',
  empty: 'empty',
  tooLarge: 'larger than 100 MB',
  duplicate: 'selected twice',
};

export function describeSkippedFiles(skipped: readonly SkippedFile[]) {
  if (skipped.length === 0) {
    return undefined;
  }

  const counts = new Map<SkipReason, number>();
  for (const file of skipped) {
    counts.set(file.reason, (counts.get(file.reason) ?? 0) + 1);
  }

  const parts = [...counts].map(
    ([reason, count]) => `${count} ${reasonDescriptions[reason]}`,
  );
  const files = skipped.length === 1 ? '1 file was' : `${skipped.length} files were`;
  return `${files} skipped: ${parts.join(', ')}.`;
}

// Turns the API's validation errors, keyed like "files[3]" or "files[3].sizeBytes", into
// messages that name the file instead of its position in the request.
export function describeRejectedFiles(
  errors: Readonly<Record<string, readonly string[]>>,
  fileNames: readonly string[],
) {
  return Object.entries(errors).flatMap(([key, messages]) => {
    const index = /^files\[(\d+)\]/.exec(key)?.[1];
    const fileName = index === undefined ? undefined : fileNames[Number(index)];
    return messages.map((message) => (fileName ? `${fileName}: ${message}` : message));
  });
}

function skipReason(file: SelectedFile): SkipReason | undefined {
  const name = file.name.trim();
  const dot = name.lastIndexOf('.');
  const extension = dot < 0 ? '' : name.slice(dot).toLowerCase();
  const contentTypes = allowedContentTypes[extension];
  if (
    name.length === 0
    || name.length > maximumFileNameLength
    || !contentTypes?.includes(file.type.trim().toLowerCase())
  ) {
    return 'unsupported';
  }

  if (file.size <= 0) {
    return 'empty';
  }

  return file.size > maximumPhotoSizeBytes ? 'tooLarge' : undefined;
}
