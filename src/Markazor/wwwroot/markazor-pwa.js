let updateTimer = null;
let updateRegistration = null;
let updateFoundHandler = null;

export async function waitForUpdate() {
  if (!("serviceWorker" in navigator)) {
    return false;
  }

  const registration = await navigator.serviceWorker.ready;
  updateRegistration = registration;

  if (registration.waiting) {
    return true;
  }

  return new Promise(resolve => {
    const finish = () => {
      clearUpdateWatch();
      resolve(true);
    };

    const watchInstallingWorker = worker => {
      if (!worker) {
        return;
      }

      const stateHandler = () => {
        if (worker.state === "installed" && navigator.serviceWorker.controller) {
          worker.removeEventListener("statechange", stateHandler);
          finish();
        }
      };

      worker.addEventListener("statechange", stateHandler);
    };

    updateFoundHandler = () => watchInstallingWorker(registration.installing);
    registration.addEventListener("updatefound", updateFoundHandler);
    const requestUpdate = () => registration.update().catch(() => {});
    updateTimer = setInterval(requestUpdate, 60_000);
    requestUpdate();
  });
}

export async function activateUpdate() {
  const reload = () => window.location.reload();

  if (!("serviceWorker" in navigator)) {
    reload();
    return true;
  }

  const registration = updateRegistration ?? await navigator.serviceWorker.ready;
  if (!registration.waiting) {
    reload();
    return true;
  }

  await new Promise(resolve => {
    let isResolved = false;
    const finish = () => {
      if (isResolved) {
        return;
      }

      isResolved = true;
      resolve();
      reload();
    };

    navigator.serviceWorker.addEventListener("controllerchange", () => {
      finish();
    }, { once: true });

    registration.waiting.postMessage({ type: "SKIP_WAITING" });
    setTimeout(finish, 5_000);
  });

  return true;
}

export function dispose() {
  clearUpdateWatch();
}

function clearUpdateWatch() {
  if (updateTimer !== null) {
    clearInterval(updateTimer);
    updateTimer = null;
  }

  if (updateRegistration && updateFoundHandler) {
    updateRegistration.removeEventListener("updatefound", updateFoundHandler);
  }

  updateRegistration = null;
  updateFoundHandler = null;
}
