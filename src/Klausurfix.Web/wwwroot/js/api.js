// API client for Klausurfix
export const api = {
  async request(endpoint, options = {}) {
    const headers = {
      "Content-Type": "application/json",
      "X-Requested-With": "XMLHttpRequest",
      ...(options.headers || {})
    };

    const res = await fetch(endpoint, {
      ...options,
      headers
    });

    if (res.status === 401) {
      // Signal session locked
      window.dispatchEvent(new CustomEvent("klausurfix:locked"));
      throw new Error("Sitzung ist gesperrt oder abgelaufen.");
    }

    if (!res.ok) {
      let errorMsg = `Serverfehler (${res.status})`;
      try {
        const data = await res.json();
        if (data && (data.error || data.message)) {
          errorMsg = data.error || data.message;
        }
      } catch {}
      throw new Error(errorMsg);
    }

    const contentType = res.headers.get("content-type");
    if (contentType && contentType.includes("application/json")) {
      return await res.json();
    }
    return res;
  },

  // Setup & Auth
  getSetupStatus() {
    return this.request("/api/setup/status");
  },
  generateRecoveryKey() {
    return this.request("/api/setup/generate-recovery-key");
  },
  initSetup(pin, recoveryKey, timeoutMinutes) {
    return this.request("/api/setup/init", {
      method: "POST",
      body: JSON.stringify({ pin, recoveryKey, inactivityTimeoutMinutes: timeoutMinutes })
    });
  },
  unlockPin(pin) {
    return this.request("/api/auth/unlock-pin", {
      method: "POST",
      body: JSON.stringify({ pin })
    });
  },
  unlockRecovery(recoveryKey) {
    return this.request("/api/auth/unlock-recovery", {
      method: "POST",
      body: JSON.stringify({ recoveryKey })
    });
  },
  unlockHello() {
    return this.request("/api/auth/unlock-hello", { method: "POST" });
  },
  lock() {
    return this.request("/api/auth/lock", { method: "POST" });
  },
  shutdown(roundId) {
    return this.request("/api/application/shutdown", { method: "POST", body: JSON.stringify({ roundId }) });
  },
  changePin(oldPin, newPin) {
    return this.request("/api/auth/change-pin", {
      method: "POST",
      body: JSON.stringify({ oldPin, newPin })
    });
  },
  registerHello() {
    return this.request("/api/auth/hello/register", { method: "POST" });
  },
  removeHello(pin) {
    return this.request("/api/auth/hello/remove", { method: "POST", body: JSON.stringify({ pin }) });
  },

  // Schulformen
  getSchulformen(includeArchived = false) {
    return this.request(`/api/schulformen?includeArchived=${includeArchived}`);
  },
  getSchulform(id) {
    return this.request(`/api/schulformen/${id}`);
  },
  saveSchulform(schulform) {
    return this.request("/api/schulformen", {
      method: "POST",
      body: JSON.stringify(schulform)
    });
  },
  deleteSchulform(id) {
    return this.request(`/api/schulformen/${id}`, { method: "DELETE" });
  },
  previewDeduction(words, errors, tiers, maxDeduction) {
    return this.request("/api/schulformen/preview-deduction", {
      method: "POST",
      body: JSON.stringify({ words, errors, tiers, maxDeduction })
    });
  },
  previewGrade(percentage, gradeKey, system, deduction) {
    return this.request("/api/schulformen/preview-grade", {
      method: "POST",
      body: JSON.stringify({ percentage, gradeKey, system, deduction })
    });
  },

  // Klassen & Schüler
  getClasses(includeArchived = false) {
    return this.request(`/api/klassen?includeArchived=${includeArchived}`);
  },
  getClass(id) {
    return this.request(`/api/klassen/${id}`);
  },
  saveClass(classGroup) {
    return this.request("/api/klassen", {
      method: "POST",
      body: JSON.stringify(classGroup)
    });
  },
  deleteClass(id) {
    return this.request(`/api/klassen/${id}`, { method: "DELETE" });
  },
  getStudents(classId, includeArchived = false) {
    return this.request(`/api/klassen/${classId}/students?includeArchived=${includeArchived}`);
  },
  saveStudent(classId, student) {
    return this.request(`/api/klassen/${classId}/students`, {
      method: "POST",
      body: JSON.stringify(student)
    });
  },
  reorderStudents(classId, studentIds) {
    return this.request(`/api/klassen/${classId}/students/reihenfolge`, { method: "PUT", body: JSON.stringify({ studentIds }) });
  },
  deleteStudent(classId, studentId) {
    return this.request(`/api/klassen/${classId}/students/${studentId}`, { method: "DELETE" });
  },

  // Klausuren
  getExams(archived = false) {
    return this.request(archived ? "/api/klausuren?archived=true" : "/api/klausuren");
  },
  setExamArchived(id, archived) {
    return this.request(`/api/klausuren/${encodeURIComponent(id)}/archivierung`, {
      method: "PUT", body: JSON.stringify({ archived })
    });
  },
  getExam(id) {
    return this.request(`/api/klausuren/${id}`);
  },
  getCoverSheet(id) {
    return this.request(`/api/klausuren/${encodeURIComponent(id)}/deckblatt`);
  },
  getOperators() {
    return this.request("/api/deckblatt/operatoren");
  },
  saveCoverSheet(id, settings) {
    return this.request(`/api/klausuren/${encodeURIComponent(id)}/deckblatt`, {
      method: "PUT", body: JSON.stringify(settings)
    });
  },
  createExam(classId, title, date, tasks) {
    return this.request("/api/klausuren", {
      method: "POST",
      body: JSON.stringify({ classId, title, date, tasks })
    });
  },
  deleteExam(id) {
    return this.request(`/api/klausuren/${id}`, { method: "DELETE" });
  },
  saveExamEntries(id, entries) {
    return this.request(`/api/klausuren/${id}/entries`, {
      method: "POST",
      body: JSON.stringify(entries)
    });
  },
  saveExpectationHorizons(id, horizons) {
    return this.request(`/api/klausuren/${encodeURIComponent(id)}/erwartungshorizonte`, {
      method: "PUT", body: JSON.stringify(horizons)
    });
  },
  previewExamEntry(id, entry) {
    return this.request(`/api/klausuren/${id}/preview-entry`, {
      method: "POST", body: JSON.stringify(entry)
    });
  },
  getDefaultGradeKey(system) {
    return this.request(`/api/grading/defaults/${system}`);
  },
  recalculateExam(id, newProfile = null) {
    return this.request(`/api/klausuren/${id}/recalculate`, {
      method: "POST",
      body: JSON.stringify({ newProfile })
    });
  },
  getExamSummary(id) {
    return this.request(`/api/klausuren/${id}/summary`);
  },

  // Settings & Backups
  updateTimeout(minutes) {
    return this.request("/api/settings/timeout", {
      method: "POST",
      body: JSON.stringify({ timeoutMinutes: minutes })
    });
  },
  getBackups() {
    return this.request("/api/backups");
  },
  getAutomaticBackupStatus() {
    return this.request("/api/backups/automatic/status");
  },
  setAutomaticBackupSettings(enabled, intervalHours) {
    return this.request("/api/backups/automatic/settings", { method: "PUT", body: JSON.stringify({ enabled, intervalHours }) });
  },
  createBackup() {
    return this.request("/api/backups/create", {
      method: "POST"
    });
  },
  createUpdateBackup(roundId) {
    return this.request("/api/update/coordinated-backup", { method: "POST", body: JSON.stringify({ roundId }) });
  },
  downloadBackup() {
    return this.request("/api/backups/download", { method: "POST" });
  },
  restoreBackup(fileName, recoveryKey) {
    return this.request(`/api/backups/${encodeURIComponent(fileName)}/restore`, {
      method: "POST",
      body: JSON.stringify({ recoveryKey })
    });
  },
  restoreBackupFile(dataBase64, recoveryKey) {
    return this.request("/api/backups/restore-file", {
      method: "POST", body: JSON.stringify({ dataBase64, recoveryKey })
    });
  },
  deleteBackup(fileName) {
    return this.request(`/api/backups/${encodeURIComponent(fileName)}`, { method: "DELETE" });
  },
  dangerClearActiveData(pin) {
    return this.request("/api/settings/danger/clear-active-data", {
      method: "POST",
      body: JSON.stringify({ pin })
    });
  },
  dangerDeleteAllBackups(pin) {
    return this.request("/api/settings/danger/delete-all-backups", {
      method: "POST",
      body: JSON.stringify({ pin })
    });
  },
  dangerFullReset(pin) {
    return this.request("/api/settings/danger/full-reset", {
      method: "POST",
      body: JSON.stringify({ pin })
    });
  }
};

