// Remembers the active import per trip so a reload or a new tab can resume it: the user
// reselects the files, and their fingerprints are matched to the items still awaiting upload.
export interface StoredImport {
  batchId: string;
  clientBatchId: string;
}

function storageKey(tripId: string) {
  return `travel-memory:photo-import:${tripId}`;
}

export function readStoredImport(tripId: string): StoredImport | undefined {
  const value = localStorage.getItem(storageKey(tripId));
  if (!value) {
    return undefined;
  }

  try {
    return JSON.parse(value) as StoredImport;
  } catch {
    localStorage.removeItem(storageKey(tripId));
    return undefined;
  }
}

export function saveStoredImport(tripId: string, storedImport: StoredImport) {
  localStorage.setItem(storageKey(tripId), JSON.stringify(storedImport));
}

export function clearStoredImport(tripId: string) {
  localStorage.removeItem(storageKey(tripId));
}
