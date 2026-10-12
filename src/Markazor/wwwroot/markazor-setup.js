const storageKeyPrefix = "markazor.setup.";

export function getLocalValue(key) {
  return window.localStorage.getItem(storageKeyPrefix + key);
}

export function setLocalValue(key, value) {
  const storageKey = storageKeyPrefix + key;

  if (!value) {
    window.localStorage.removeItem(storageKey);
    return;
  }

  window.localStorage.setItem(storageKey, value);
}

export async function copyText(value) {
  try {
    await navigator.clipboard.writeText(value);
    return true;
  } catch {
    return false;
  }
}
