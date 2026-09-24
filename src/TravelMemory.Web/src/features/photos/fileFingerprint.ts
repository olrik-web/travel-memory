export async function createFileFingerprint(file: File) {
  const identity = `${file.name}\n${file.size}\n${file.lastModified}`;
  const hash = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(identity),
  );
  return Array.from(new Uint8Array(hash), (value) =>
    value.toString(16).padStart(2, '0'),
  ).join('');
}
