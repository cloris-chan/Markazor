const objectUrls = new Map();

export function createObjectUrl(key, contentType, bytes) {
  revokeObjectUrl(key);

  const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
  const objectUrl = URL.createObjectURL(blob);
  objectUrls.set(key, objectUrl);

  return objectUrl;
}

export function revokeObjectUrl(key) {
  const objectUrl = objectUrls.get(key);
  if (!objectUrl) {
    return;
  }

  URL.revokeObjectURL(objectUrl);
  objectUrls.delete(key);
}

export function revokeAllObjectUrls() {
  for (const objectUrl of objectUrls.values()) {
    URL.revokeObjectURL(objectUrl);
  }

  objectUrls.clear();
}
