// Server coordination includes registered views across browser profiles and origins.
// A completed round does not by itself authorize installation.
export function createUpdateSaveCoordinator({ save, report, freeze }) {
  let viewId = null;
  let running = false;
  let closed = false;
  let currentRound = null;
  let heldRound = null;
  let unfreeze = null;
  let freezeTimer = null;
  const shutdownChannel = typeof BroadcastChannel === "function" ? new BroadcastChannel("klausurfix-shutdown") : null;
  function releaseFreeze() {
    clearTimeout(freezeTimer);
    freezeTimer = null;
    unfreeze?.();
    unfreeze = null;
    heldRound = null;
  }
  const headers = { "Content-Type": "application/json", "X-Requested-With": "XMLHttpRequest" };

  async function post(path, data = {}, signal) {
    const response = await fetch(path, { method: "POST", headers, body: JSON.stringify(data), cache: "no-store", signal });
    if (!response.ok) {
      let message = "Speicherabstimmung ist nicht erreichbar.";
      if ((response.headers.get("content-type") || "").includes("application/json")) {
        const body = await response.json().catch(() => null);
        if (typeof body?.error === "string") message = body.error;
      }
      throw Object.assign(new Error(message), { status: response.status });
    }
    return (response.headers.get("content-type") || "").includes("application/json") ? response.json() : null;
  }
  const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
  function completeShutdown(notifyOthers) {
    if (closed) return;
    closed = true;
    releaseFreeze();
    if (notifyOthers) shutdownChannel?.postMessage("shutdown-complete");
    shutdownChannel?.close();
    window.dispatchEvent(new Event("klausurfix:shutdown-complete"));
  }
  if (shutdownChannel) shutdownChannel.onmessage = event => {
    if (event.data === "shutdown-complete") completeShutdown(false);
  };
  async function register() {
    const result = await post("/api/update/view/register");
    viewId = result.viewId;
  }
  async function processRound(roundId) {
    if (closed || running || !viewId || heldRound === roundId) return;
    running = true;
    let saved = false;
    try {
      report("Speicherung vor dem Beenden wird angefordert …");
      unfreeze = freeze();
      freezeTimer = setTimeout(() => {
        releaseFreeze();
        report("Die Speicherabstimmung ist abgelaufen. Bitte erneut versuchen.", true);
      }, 45000);
      await save();
      if (closed) return;
      saved = unfreeze !== null;
    } catch {
      releaseFreeze();
      report("Speichern nicht möglich. Bitte offene Formulare und Eingaben prüfen.", true);
    }
    try {
      await post("/api/update/view/respond", { viewId, roundId, saved });
      heldRound = roundId;
    } catch (err) {
      releaseFreeze();
      throw err;
    } finally { running = false; }
  }
  async function poll() {
    while (!closed) {
      try {
        if (!viewId) await register();
        const result = await post("/api/update/view/poll", { viewId });
        if (closed) return;
        if (heldRound && result.roundId !== heldRound) {
          releaseFreeze();
          report("Die Speicherabstimmung ist beendet. Eingaben sind wieder möglich.");
        }
        // Keep polling while a save is in progress so the server can distinguish
        // a slow save from a view that has stopped responding.
        if (result.roundId) void processRound(result.roundId).catch(() => {
          releaseFreeze();
          report("Die Speicherabstimmung konnte nicht bestätigt werden. Bitte erneut versuchen.", true);
        });
      } catch (err) {
        if (err.status === 404) {
          releaseFreeze();
          viewId = null;
        }
      }
      await delay(1000);
    }
  }
  void poll();
  window.addEventListener("pagehide", event => {
    if (event.persisted) return;
    closed = true;
    releaseFreeze();
    shutdownChannel?.close();
    if (viewId) void fetch("/api/update/view/unregister", {
      method: "POST", headers, body: JSON.stringify({ viewId }), keepalive: true
    }).catch(() => {});
  });
  return {
    async request(afterSave, { shutdown = false } = {}) {
      if (currentRound) throw new Error("Eine Speicheraufforderung läuft bereits.");
      if (!viewId) throw new Error("Dieser Tab konnte noch nicht registriert werden. Bitte erneut versuchen.");
      const result = await post("/api/update/save-round/begin");
      currentRound = result.roundId;
      let shutdownComplete = false;
      try {
        const deadline = Date.now() + 15000;
        while (Date.now() < deadline) {
          const status = await post("/api/update/save-round/status", { roundId: currentRound });
          if (status.failed) throw new Error("Eine Ansicht konnte nicht speichern oder die geöffneten Ansichten haben sich geändert.");
          if (status.complete) {
            const result = await afterSave(currentRound);
            if (shutdown) {
              shutdownComplete = true;
              completeShutdown(true);
              return { count: status.expected, result };
            }
            const checked = await post("/api/update/save-round/status", { roundId: currentRound });
            if (checked.failed || !checked.complete)
              throw new Error("Die Sicherung wurde erstellt, aber die Speicherabstimmung ist abgelaufen oder hat sich geändert. Bitte erneut versuchen.");
            return { count: status.expected, result };
          }
          await delay(500);
        }
        throw new Error("Nicht alle registrierten Ansichten haben geantwortet. Bitte alle Ansichten prüfen und erneut versuchen.");
      } finally {
        const roundId = currentRound;
        currentRound = null;
        if (!shutdownComplete) {
          const controller = new AbortController();
          const timeout = setTimeout(() => controller.abort(), 2000);
          try { await post("/api/update/save-round/cancel", { roundId }, controller.signal); } catch { /* The server may have restarted. */ }
          finally { clearTimeout(timeout); releaseFreeze(); }
        }
      }
    }
  };
}
