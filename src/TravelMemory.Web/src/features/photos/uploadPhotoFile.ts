import { completePhotoUpload, renewUploadGrant } from './photoImportApi';
import type { PhotoImportItem } from './types';

// Uploads one file straight to Blob Storage with its short-lived SAS URL, renewing the URL
// when it has expired or Storage rejects it, then tells the API the upload is complete.
export async function uploadPhotoFile(batchId: string, item: PhotoImportItem, file: File) {
  let uploadUrl = item.uploadUrl;
  if (!uploadUrl || !item.uploadExpiresAtUtc || new Date(item.uploadExpiresAtUtc) <= new Date()) {
    uploadUrl = (await renewUploadGrant(batchId, item.id)).uploadUrl;
  }

  let response = await putBlob(uploadUrl, item.contentType, file);
  if (response.status === 403) {
    const renewed = await renewUploadGrant(batchId, item.id);
    response = await putBlob(renewed.uploadUrl, item.contentType, file);
  }

  if (!response.ok) {
    throw new Error(
      `${item.fileName}: upload failed with status ${response.status}. Check your connection and select the file again.`,
    );
  }

  await completePhotoUpload(batchId, item.id);
}

function putBlob(uploadUrl: string, contentType: string, file: File) {
  return fetch(uploadUrl, {
    method: 'PUT',
    headers: {
      'x-ms-blob-type': 'BlockBlob',
      'Content-Type': contentType,
    },
    body: file,
  });
}
