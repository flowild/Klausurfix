import { api } from "./api.js";
import { registerHello, unlockHello } from "./hello.js";
import { createCoverSheetEditor } from "./cover-sheet.js";
import { showHamster, hideHamster } from "./hamster.js";

// Global App State
const state = {
  isInitialized: false,
  isUnlocked: false,
  currentView: "dashboard",
  currentExamId: null,
  examListView: "dashboard",
  activeExam: null,
  activeExamEntries: [],
  classes: [],
  schulformen: [],
  exams: [],
  pinBuffer: "",
  saveTimeout: null,
  lockoutInterval: null
};
const collapsedClasses = new Set();
function updateHelloControls(status) {
  const enabled = status.isHelloAvailable && window.isSecureContext && !!window.PublicKeyCredential;
  const unlock = document.getElementById("unlockHelloBtn");
  unlock.disabled = !enabled || !status.isHelloRegistered;
  unlock.textContent = unlock.disabled ? "Geräteanmeldung derzeit nicht verfügbar" : "Mit Geräteanmeldung entsperren";
  const register = document.getElementById("registerHelloBtn");
  register.disabled = !enabled;
  register.textContent = enabled ? "Geräteanmeldung einrichten" : "Geräteanmeldung derzeit nicht verfügbar";
  document.getElementById("helloPinGroup").hidden = !enabled;
  document.getElementById("removeHelloBtn").disabled = !enabled || !status.isHelloRegistered;
}
const themeToggle = document.getElementById("themeToggle");
function updateThemeButton() {
  const dark = document.documentElement.dataset.theme === "dark";
  themeToggle.setAttribute("aria-pressed", String(dark));
  themeToggle.innerHTML = dark ? '☀ <span>Hellmodus</span>' : '☾ <span>Darkmode</span>';
}
themeToggle.addEventListener("click", () => {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem("klausurfix-theme", next); } catch {}
  updateThemeButton();
});
updateThemeButton();

// Formatting helpers
const formatNumber = (num, decimals = 1) => {
  if (num === null || num === undefined || isNaN(num)) return "-";
  return Number(num).toLocaleString("de-DE", {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals
  });
};

const parseGermanNumber = (str) => {
  if (!str) return null;
  const normalized = str.toString().trim().replace(",", ".");
  if (!/^\d+(?:\.\d+)?$/.test(normalized)) return null;
  const val = Number(normalized);
  return isNaN(val) ? null : val;
};

const formatExamDate = date => {
  const [year, month, day] = date.split("-").map(Number);
  return new Intl.DateTimeFormat("de-DE", { weekday: "long", day: "2-digit", month: "2-digit", year: "numeric" })
    .format(new Date(year, month - 1, day, 12));
};

export function showToast(message, type = "info") {
  const container = document.getElementById("toastContainer");
  const toast = document.createElement("div");
  toast.className = `toast ${type}`;
  toast.textContent = message;
  container.appendChild(toast);
  setTimeout(() => {
    toast.style.opacity = "0";
    setTimeout(() => toast.remove(), 250);
  }, 3500);
}

const coverSheetEditor = createCoverSheetEditor({ api, notify: showToast, beforeOpen: flushPendingSave });
const coverSheetOffer = document.getElementById("coverSheetOffer");
for (const id of ["matrixCoverSheetBtn", "auswertungCoverSheetBtn"]) {
  document.getElementById(id).addEventListener("click", () => {
    if (state.activeExam?.id === state.currentExamId)
      coverSheetEditor.open(state.currentExamId, { readOnly: state.activeExam.isArchived });
  });
}

// Router & View Switcher
export function switchView(viewName, historyMode = "push") {
  if (viewName !== "eingabetabelle") hideHamster();
  if (viewName !== "eingabetabelle" && coverSheetOffer.open) coverSheetOffer.close();
  const sensitiveView = viewName === "setup" || viewName === "unlock" || viewName === "stopped";
  const route = { klausurfix: true, view: viewName, examId: ["eingabetabelle", "auswertung"].includes(viewName) ? state.currentExamId : null,
    examListView: state.examListView };
  if (historyMode !== "none") {
    const url = `#/${viewName}${route.examId ? `/${encodeURIComponent(route.examId)}` : ""}`;
    const current = history.state;
    if (historyMode === "replace" || sensitiveView || !current?.klausurfix) history.replaceState(route, "", url);
    else if (current.view !== route.view || current.examId !== route.examId) history.pushState(route, "", url);
  }
  state.currentView = viewName;
  document.querySelectorAll(".view").forEach(el => el.classList.remove("active"));
  document.querySelectorAll(".nav-btn").forEach(el => {
    el.classList.toggle("active", el.dataset.view === (viewName === "archiv" ? "dashboard" : viewName));
  });

  const header = document.getElementById("appHeader");
  if (viewName === "setup" || viewName === "unlock" || viewName === "stopped") {
    header.style.display = "none";
  } else {
    header.style.display = "block";
  }

  const target = document.getElementById(`${viewName}View`);
  if (target) {
    target.classList.add("active");
  }

  // Load view-specific data
  if (viewName === "dashboard") loadDashboard();
  else if (viewName === "archiv") loadExamArchive();
  else if (viewName === "schulformen") loadSchulformen();
  else if (viewName === "klassen") loadKlassen();
  else if (viewName === "klausurNeu") prepareNewExamForm();
  else if (viewName === "settings") loadSettings();
}

window.addEventListener("popstate", async event => {
  const route = event.state;
  if (!state.isUnlocked || !route?.klausurfix) {
    switchView(state.isInitialized ? "unlock" : "setup", "replace");
    return;
  }
  if (state.currentView === "eingabetabelle") {
    try { await flushPendingSave(); }
    catch (err) {
      showToast(err.message || "Bitte die Eingaben prüfen.", "error");
      const current = { klausurfix: true, view: "eingabetabelle", examId: state.currentExamId, examListView: state.examListView };
      history.pushState(current, "", `#/eingabetabelle/${encodeURIComponent(state.currentExamId)}`);
      return;
    }
  }
  if (["eingabetabelle", "auswertung"].includes(route.view)) state.examListView = route.examListView === "archiv" ? "archiv" : "dashboard";
  if (route.view === "eingabetabelle" && route.examId) openExamMatrix(route.examId, true);
  else if (route.view === "auswertung" && route.examId) openExamAuswertung(route.examId, true);
  else if (["dashboard", "archiv", "klassen", "schulformen", "klausurNeu", "settings"].includes(route.view)) switchView(route.view, "none");
  else switchView("dashboard", "replace");
});

// Global initialization
async function init() {
  try {
    const status = await api.getSetupStatus();
    updateHelloControls(status);
    state.isInitialized = status.isInitialized;
    state.isUnlocked = status.isUnlocked;

    if (!state.isInitialized) {
      // Prepare setup view
      const rec = await api.generateRecoveryKey();
      document.getElementById("setupRecoveryKey").value = rec.recoveryKey;
      switchView("setup");
    } else if (!state.isUnlocked) {
      switchView("unlock");
    } else {
      const route = history.state;
      if (["eingabetabelle", "auswertung"].includes(route?.view)) state.examListView = route.examListView === "archiv" ? "archiv" : "dashboard";
      if (route?.klausurfix && route.view === "eingabetabelle" && route.examId) openExamMatrix(route.examId, true);
      else if (route?.klausurfix && route.view === "auswertung" && route.examId) openExamAuswertung(route.examId, true);
      else if (route?.klausurfix && ["dashboard", "archiv", "klassen", "schulformen", "klausurNeu", "settings"].includes(route.view)) switchView(route.view, "none");
      else switchView("dashboard", "replace");
    }
  } catch (err) {
    console.error("Init failed:", err);
    switchView("unlock");
  }
}

// Setup Form Handler
document.getElementById("setupForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const pin = document.getElementById("setupPin").value;
  const pinConfirm = document.getElementById("setupPinConfirm").value;
  const recoveryKey = document.getElementById("setupRecoveryKey").value;
  const timeout = parseInt(document.getElementById("setupTimeout").value, 10);

  if (pin !== pinConfirm) {
    showToast("Die eingegebenen PINs stimmen nicht überein.", "error");
    return;
  }
  if (pin.length < 6) {
    showToast("Die PIN muss mindestens 6 Stellen haben.", "error");
    return;
  }

  try {
    await api.initSetup(pin, recoveryKey, timeout);
    state.isInitialized = true;
    state.isUnlocked = true;
    showToast("Klausurfix wurde erfolgreich eingerichtet!", "success");
    switchView("dashboard");
  } catch (err) {
    showToast(err.message, "error");
  }
});

document.getElementById("copyRecoveryKeyBtn").addEventListener("click", () => {
  const val = document.getElementById("setupRecoveryKey").value;
  navigator.clipboard.writeText(val);
  showToast("Wiederherstellungsschlüssel in die Zwischenablage kopiert.", "info");
});

// Unlock Screen Logic (Numpad + Keyboard)
function updatePinDots() {
  const display = document.getElementById("pinDisplay");
  display.replaceChildren(...Array.from({ length: Math.max(6, state.pinBuffer.length) }, (_, idx) => {
    const dot = document.createElement("div");
    dot.className = idx < state.pinBuffer.length ? "pin-dot filled" : "pin-dot";
    return dot;
  }));
}

async function handlePinInput(digit) {
  if (state.pinBuffer.length >= 64) return;
  state.pinBuffer += digit;
  updatePinDots();

}

document.getElementById("submitPinBtn").addEventListener("click", submitPin);

function handlePinBackspace() {
  if (state.pinBuffer.length > 0) {
    state.pinBuffer = state.pinBuffer.slice(0, -1);
    updatePinDots();
  }
}

function handlePinClear() {
  state.pinBuffer = "";
  updatePinDots();
}

async function submitPin() {
  const pin = state.pinBuffer;
  const msgEl = document.getElementById("unlockMessage");
  msgEl.textContent = "";

  try {
    const res = await api.unlockPin(pin);
    if (res.success) {
      state.pinBuffer = "";
      updatePinDots();
      state.isUnlocked = true;
      showToast("Erfolgreich entsperrt.", "success");
      switchView("dashboard");
    }
  } catch (err) {
    state.pinBuffer = "";
    updatePinDots();
    msgEl.textContent = err.message;
  }
}

// Numpad clicks
document.querySelectorAll(".numpad-btn").forEach(btn => {
  btn.addEventListener("click", () => {
    const key = btn.dataset.key;
    if (key === "backspace") handlePinBackspace();
    else if (key === "clear") handlePinClear();
    else handlePinInput(key);
  });
});

// Physical Keyboard input on unlock screen
window.addEventListener("keydown", (e) => {
  if (state.currentView !== "unlock") return;
  if (e.key >= "0" && e.key <= "9") {
    handlePinInput(e.key);
  } else if (e.key === "Backspace") {
    handlePinBackspace();
  } else if (e.key === "Escape") {
    handlePinClear();
  } else if (e.key === "Enter") {
    submitPin();
  }
});

// Windows Hello Unlock
document.getElementById("unlockHelloBtn").addEventListener("click", async () => {
  const msgEl = document.getElementById("unlockMessage");
  msgEl.textContent = "";
  try {
    const res = await unlockHello();
    if (res.success) {
      state.isUnlocked = true;
      showToast("Mit Windows Hello entsperrt.", "success");
      switchView("dashboard");
    }
  } catch (err) {
    msgEl.textContent = err.message;
  }
});

// Manual Lock
document.getElementById("lockBtn").addEventListener("click", async () => {
  try {
    await flushPendingSave();
    await api.lock();
    window.dispatchEvent(new CustomEvent("klausurfix:locked"));
    showToast("Anwendung gesperrt.", "info");
  } catch (err) {
    console.error(err);
  }
});

document.getElementById("shutdownBtn").addEventListener("click", async () => {
  try {
    await flushPendingSave();
    if (!confirm("Klausurfix jetzt beenden?")) return;
    await api.shutdown();
    state.isUnlocked = false;
    state.activeExam = null;
    state.activeExamEntries = [];
    state.classes = [];
    state.exams = [];
    state.examListView = "dashboard";
    document.getElementById("matrixTableContainer").replaceChildren();
    document.getElementById("dashboardExamsList").replaceChildren();
    document.getElementById("archiveExamsList").replaceChildren();
    switchView("stopped");
  } catch (err) {
    showToast(err.message || "Klausurfix konnte nicht beendet werden.", "error");
  }
});

window.addEventListener("klausurfix:locked", () => {
  state.isUnlocked = false;
  state.pinBuffer = "";
  updatePinDots();
  state.activeExam = null;
  state.activeExamEntries = [];
  state.classes = [];
  state.exams = [];
  state.examListView = "dashboard";
  for (const id of ["matrixTableContainer", "backupsListTable", "dashboardExamsList", "archiveExamsList"]) {
    document.getElementById(id).replaceChildren();
  }
  switchView("unlock");
});

// Status requests do not extend the server inactivity deadline.
setInterval(async () => {
  if (!state.isUnlocked) return;
  try {
    const status = await api.getSetupStatus();
    updateHelloControls(status);
    if (!status.isUnlocked) window.dispatchEvent(new CustomEvent("klausurfix:locked"));
  } catch { window.dispatchEvent(new CustomEvent("klausurfix:locked")); }
}, 5000);

// Navigation links
document.querySelectorAll(".nav-btn").forEach(btn => {
  btn.addEventListener("click", () => {
    const view = btn.dataset.view;
    if (view) switchView(view);
  });
});

document.getElementById("logoBtn").addEventListener("click", () => switchView("dashboard"));

// ==========================================
// Klausurenübersicht
// ==========================================
let examListRevision = 0;

async function loadDashboard() {
  await loadExamList(false);
}

async function loadExamArchive() {
  await loadExamList(true);
}

async function loadExamList(archived) {
  const revision = ++examListRevision;
  try {
    const exams = await api.getExams(archived);
    if (revision !== examListRevision || state.currentView !== (archived ? "archiv" : "dashboard")) return;

    const tbody = document.getElementById(archived ? "archiveExamsList" : "dashboardExamsList");
    tbody.innerHTML = "";

    if (exams.length === 0) {
      tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--gray-500); padding: 2rem;">${archived ? "Noch keine Klausuren archiviert." : "Noch keine aktiven Klausuren vorhanden."}</td></tr>`;
      return;
    }

    exams.forEach(ex => {
      const tr = document.createElement("tr");
      tr.className = "exam-row";
      tr.innerHTML = `
        <td><a class="exam-row-link" href="#/eingabetabelle/${encodeURIComponent(ex.id)}" aria-label="${escapeHtml(ex.title)}, ${escapeHtml(ex.className)}: Eingabetabelle öffnen">${escapeHtml(ex.title)}</a>${archived ? ' <span class="archive-badge">Archiviert</span>' : ""}</td>
        <td>${escapeHtml(ex.className)}</td>
        <td>${escapeHtml(ex.schulformName)}</td>
        <td>${escapeHtml(formatExamDate(ex.date))}</td>
        <td>${ex.tasks.length} ${ex.tasks.length === 1 ? "Aufgabe" : "Aufgaben"}</td>
        <td>${formatNumber(ex.totalMaxPoints, 1)} P</td>
        <td class="exam-archive-cell"><button type="button" class="btn btn-secondary exam-archive-btn" aria-label="${escapeHtml(ex.title)}, ${escapeHtml(ex.className)}: ${archived ? "wiederherstellen" : "archivieren"}">${archived ? "Wiederherstellen" : "Archivieren"}</button></td>
      `;
      tr.addEventListener("click", event => {
        if (event.target.closest("button")) return;
        if (event.target.closest("a")) event.preventDefault();
        state.examListView = archived ? "archiv" : "dashboard";
        openExamMatrix(ex.id);
      });
      const archiveButton = tr.querySelector(".exam-archive-btn");
      archiveButton.addEventListener("click", async event => {
        event.stopPropagation();
        archiveButton.disabled = true;
        try {
          await api.setExamArchived(ex.id, !archived);
          if (state.currentView === (archived ? "archiv" : "dashboard")) {
            const restoreFocus = document.activeElement === archiveButton;
            tr.remove();
            if (!tbody.querySelector(".exam-row"))
              tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--gray-500); padding: 2rem;">${archived ? "Noch keine Klausuren archiviert." : "Noch keine aktiven Klausuren vorhanden."}</td></tr>`;
            await loadExamList(archived);
            if (restoreFocus && state.currentView === (archived ? "archiv" : "dashboard") && document.activeElement === document.body)
              (tbody.querySelector(".exam-archive-btn") || document.getElementById(archived ? "archiveBackBtn" : "openExamArchiveBtn")).focus();
          }
          showToast(archived ? "Klausur wiederhergestellt." : "Klausur archiviert.", "success");
        } catch (err) { showToast(err.message, "error"); }
        finally { archiveButton.disabled = false; }
      });
      tbody.appendChild(tr);
    });
  } catch (err) {
    if (revision === examListRevision && state.currentView === (archived ? "archiv" : "dashboard"))
      showToast(err.message, "error");
  }
}

document.getElementById("dashNewExamBtn").addEventListener("click", () => switchView("klausurNeu"));
document.getElementById("openExamArchiveBtn").addEventListener("click", () => switchView("archiv"));
document.getElementById("archiveBackBtn").addEventListener("click", () => switchView("dashboard"));

// ==========================================
// Schulformen & Bewertungsprofile
// ==========================================
async function loadSchulformen() {
  try {
    const list = await api.getSchulformen();
    state.schulformen = list;

    const container = document.getElementById("schulformenGrid");
    container.innerHTML = "";

    list.forEach(sf => {
      const card = document.createElement("div");
      card.className = "card";

      const systemLabel = sf.profile.system === 1 ? "Schulnoten (1–6)" : "Notenpunkte (0–15)";
      const unitLabel = sf.profile.deductionUnit === 1 ? "Schulnoten" : sf.profile.deductionUnit === 2 ? "Notenpunkte" : "% der Gesamtpunkte";

      let tiersHtml = sf.profile.deductionTiers.map(t =>
        `<li>ab FQ <strong>${formatNumber(t.threshold, 2)}</strong>: <strong>-${formatNumber(t.deduction, 1)}</strong> ${unitLabel}</li>`
      ).join("");

      card.innerHTML = `
        <div class="card-header" style="margin-bottom: 0.75rem;">
          <div>
            <h2 class="card-title" style="font-size: 1.25rem;">${escapeHtml(sf.name)}</h2>
            <p class="card-subtitle">${systemLabel} | Abzug in ${unitLabel}</p>
          </div>
          <button class="btn btn-secondary btn-sm edit-sf-btn" data-id="${sf.id}">Bearbeiten</button>
        </div>

        <div style="font-size: 0.9rem; color: var(--gray-700);">
          <p style="font-weight: 600; margin-bottom: 0.25rem;">Rechtschreib-Abzugsstufen (Fehlerquotient):</p>
          <ul style="padding-left: 1.25rem; margin-bottom: 0.75rem;">
            ${tiersHtml || "<li>Keine Abzüge konfiguriert</li>"}
          </ul>
          <p style="font-weight: 600; margin-bottom: 0.25rem;">Notenschlüssel (${sf.profile.gradeKey.length} Stufen):</p>
          <p style="color: var(--gray-500); font-size: 0.85rem;">z. B. ab ${formatNumber(sf.profile.gradeKey[0]?.minPercentage, 1)}% (${escapeHtml(sf.profile.gradeKey[0]?.label || '')}) bis 0% (${escapeHtml(sf.profile.gradeKey[sf.profile.gradeKey.length - 1]?.label || '')}). Bitte die Notengrenzen für Ihre Schule prüfen.</p>
        </div>
      `;

      container.appendChild(card);
    });

    container.querySelectorAll(".edit-sf-btn").forEach(btn => {
      btn.addEventListener("click", () => openEditSchulformModal(btn.dataset.id));
    });
  } catch (err) {
    showToast(err.message, "error");
  }
}

document.getElementById("newSchulformBtn").addEventListener("click", () => openEditSchulformModal(null));

function openEditSchulformModal(id) {
  const sf = id ? state.schulformen.find(x => x.id === id) : null;
  const isNew = !sf;

  let currentProfile = sf ? JSON.parse(JSON.stringify(sf.profile)) : {
    system: 1,
    deductionUnit: 1,
    maxDeduction: null,
    deductionTiers: [
      { threshold: 2.0, deduction: 1.0 },
      { threshold: 4.0, deduction: 2.0 }
    ],
    gradeKey: [
      { minPercentage: 87.5, gradeValue: 1, label: "Sehr gut (1)" },
      { minPercentage: 75.0, gradeValue: 2, label: "Gut (2)" },
      { minPercentage: 62.5, gradeValue: 3, label: "Befriedigend (3)" },
      { minPercentage: 50.0, gradeValue: 4, label: "Ausreichend (4)" },
      { minPercentage: 25.0, gradeValue: 5, label: "Mangelhaft (5)" },
      { minPercentage: 0.0,  gradeValue: 6, label: "Ungenügend (6)" }
    ]
  };

  const modal = document.createElement("div");
  modal.className = "modal-backdrop";
  modal.innerHTML = `
    <div class="modal-content" style="max-width: 680px;">
      <h2 class="modal-title">${isNew ? "Neue Schulform" : "Schulform bearbeiten"}</h2>
      <form id="editSfForm">
        <div class="form-group">
          <label class="form-label">Bezeichnung der Schulform</label>
          <input type="text" id="sfNameInput" class="form-control" value="${escapeHtml(sf?.name || '')}" placeholder="z. B. Gymnasium Sekundarstufe I" required>
        </div>

        <div class="grid-2">
          <div class="form-group">
            <label class="form-label">Notensystem</label>
            <select id="sfSystemSelect" class="form-control">
              <option value="1" ${currentProfile.system === 1 ? 'selected' : ''}>Schulnoten (1 bis 6)</option>
              <option value="2" ${currentProfile.system === 2 ? 'selected' : ''}>Notenpunkte (0 bis 15)</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Abzugseinheit für Fehlerquotient</label>
            <select id="sfDeductionUnitSelect" class="form-control">
              <option value="1" ${currentProfile.deductionUnit === 1 ? 'selected' : ''}>Ganze Schulnoten</option>
              <option value="2" ${currentProfile.deductionUnit === 2 ? 'selected' : ''}>Notenpunkte</option>
              <option value="3" ${currentProfile.deductionUnit === 3 ? 'selected' : ''}>Prozent der maximalen Gesamtpunkte</option>
            </select>
          </div>
        </div>

        <div class="form-group" style="margin-top: 1rem;">
          <label class="form-label" for="gradeKeyInput">Notenschlüssel: Mindestprozent; Note / Notenpunkte</label>
          <textarea id="gradeKeyInput" class="form-control" rows="7" spellcheck="false">${currentProfile.gradeKey.map(b => `${b.minPercentage};${b.gradeValue}`).join('\n')}</textarea>
          <p class="form-hint">Eine Stufe pro Zeile, zum Beispiel 87,5;1. Die letzte Stufe muss bei 0 beginnen.</p>
        </div>

        <!-- Tiers Table -->
        <div style="margin-top: 1.5rem;">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
            <h4 style="font-weight: 700;">Abzugstabelle für Rechtschreibfehler</h4>
            <button type="button" class="btn btn-secondary btn-sm" id="addTierBtn">+ Stufe hinzufügen</button>
          </div>
          <p class="form-hint" style="margin-bottom: 0.75rem;">Eine Stufe gilt ab ihrer Grenze einschließlich bis zur nächsten ausschließlich. Unterhalb der 1. Grenze ist der Abzug 0.</p>
          <div class="table-responsive">
            <table class="data-table" style="font-size: 0.9rem;">
              <thead>
                <tr>
                  <th>Fehlerquotient ab</th>
                  <th>Abzug</th>
                  <th style="width: 50px;"></th>
                </tr>
              </thead>
              <tbody id="sfTiersTbody"></tbody>
            </table>
          </div>
        </div>

        <!-- Interactive Deduction Preview -->
        <div style="background-color: var(--gray-100); padding: 1rem; border-radius: var(--radius-md); margin-top: 1rem;">
          <h5 style="font-weight: 600; margin-bottom: 0.5rem;">Vorschau-Berechnung anhand eines Beispiels:</h5>
          <div style="display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap;">
            <span>Wörter:</span>
            <input type="number" id="previewWords" class="form-control" style="width: 90px; height: 36px;" value="600" min="1">
            <span>Fehler:</span>
            <input type="number" id="previewErrors" class="form-control" style="width: 80px; height: 36px;" value="12" min="0">
            <span style="font-weight: 700; color: var(--primary);" id="previewFqResult">FQ: 2.00 → Abzug: -1</span>
          </div>
        </div>

        <div style="display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 2rem;">
          <button type="button" class="btn btn-secondary" id="closeSfModalBtn">Abbrechen</button>
          <button type="submit" class="btn btn-primary">Speichern</button>
        </div>
      </form>
    </div>
  `;

  document.getElementById("modalContainer").appendChild(modal);

  function renderTiers() {
    const tbody = modal.querySelector("#sfTiersTbody");
    tbody.innerHTML = "";
    currentProfile.deductionTiers.sort((a, b) => a.threshold - b.threshold);

    currentProfile.deductionTiers.forEach((tier, index) => {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td>
          <input type="number" step="0.1" min="0" class="form-control tier-thresh" value="${tier.threshold}" style="width: 100px; height: 36px;">
        </td>
        <td>
          <input type="number" step="0.5" min="0" class="form-control tier-deduct" value="${tier.deduction}" style="width: 100px; height: 36px;">
        </td>
        <td>
          <button type="button" class="btn btn-secondary btn-sm remove-tier-btn" style="color: var(--danger);">&times;</button>
        </td>
      `;

      tr.querySelector(".tier-thresh").addEventListener("change", (e) => {
        tier.threshold = parseFloat(e.target.value) || 0;
        updatePreview();
      });
      tr.querySelector(".tier-deduct").addEventListener("change", (e) => {
        tier.deduction = parseFloat(e.target.value) || 0;
        updatePreview();
      });
      tr.querySelector(".remove-tier-btn").addEventListener("click", () => {
        currentProfile.deductionTiers.splice(index, 1);
        renderTiers();
        updatePreview();
      });

      tbody.appendChild(tr);
    });
  }

  function updatePreview() {
    const words = parseInt(modal.querySelector("#previewWords").value, 10);
    const errors = parseInt(modal.querySelector("#previewErrors").value, 10);
    if (!words || words <= 0) {
      modal.querySelector("#previewFqResult").textContent = "Ungültige Wortanzahl";
      return;
    }
    const fq = (errors * 100) / words;
    let deduction = 0;
    const sorted = [...currentProfile.deductionTiers].sort((a, b) => a.threshold - b.threshold);
    for (const t of sorted) {
      if (fq >= t.threshold) deduction = t.deduction;
    }
    modal.querySelector("#previewFqResult").textContent = `FQ: ${formatNumber(fq, 2)} → Abzug: -${formatNumber(deduction, 1)}`;
  }

  modal.querySelector("#addTierBtn").addEventListener("click", () => {
    const maxT = currentProfile.deductionTiers.length > 0
      ? Math.max(...currentProfile.deductionTiers.map(x => x.threshold)) + 2
      : 2.0;
    currentProfile.deductionTiers.push({ threshold: maxT, deduction: 1.0 });
    renderTiers();
    updatePreview();
  });

  modal.querySelector("#previewWords").addEventListener("input", updatePreview);
  modal.querySelector("#previewErrors").addEventListener("input", updatePreview);

  modal.querySelector("#sfSystemSelect").addEventListener("change", async (e) => {
    const sys = parseInt(e.target.value, 10);
    currentProfile.system = sys;
    currentProfile.deductionUnit = sys;
    modal.querySelector("#sfDeductionUnitSelect").value = sys.toString();
    try {
      currentProfile.gradeKey = await api.getDefaultGradeKey(sys);
      modal.querySelector("#gradeKeyInput").value = currentProfile.gradeKey.map(b => `${b.minPercentage};${b.gradeValue}`).join("\n");
    } catch (err) { showToast(err.message, "error"); }
  });
  modal.querySelector("#sfDeductionUnitSelect").addEventListener("change", e => {
    currentProfile.deductionUnit = Number(e.target.value);
  });

  renderTiers();
  updatePreview();

  modal.querySelector("#closeSfModalBtn").addEventListener("click", () => modal.remove());

  modal.querySelector("#editSfForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const name = modal.querySelector("#sfNameInput").value.trim();
    if (!name) return;

    try {
      const gradeLines = modal.querySelector("#gradeKeyInput").value.trim().split(/\r?\n/);
      currentProfile.gradeKey = gradeLines.map(line => {
        const parts = line.split(";");
        const minPercentage = parseGermanNumber(parts[0]);
        const gradeValue = Number(parts[1]);
        if (parts.length !== 2 || !parts[1].trim() || minPercentage === null || !Number.isInteger(gradeValue))
          throw new Error("Bitte jede Notenstufe als Mindestprozent;Note eingeben.");
        return { minPercentage, gradeValue, label: String(gradeValue) };
      });
      const payload = {
        id: sf ? sf.id : undefined,
        name,
        profile: currentProfile,
        isArchived: sf ? sf.isArchived : false
      };
      await api.saveSchulform(payload);
      showToast("Schulform gespeichert.", "success");
      modal.remove();
      loadSchulformen();
    } catch (err) {
      showToast(err.message, "error");
    }
  });
}

// ==========================================
// Klassen & Schüler
// ==========================================
async function loadKlassen() {
  try {
    const [classes, schulformen] = await Promise.all([
      api.getClasses(),
      api.getSchulformen()
    ]);
    state.classes = classes;
    state.schulformen = schulformen;

    const container = document.getElementById("klassenContainer");
    container.innerHTML = "";

    if (classes.length === 0) {
      container.innerHTML = `<div class="card" style="text-align: center; color: var(--gray-500); padding: 2rem;">Noch keine Klassen angelegt.</div>`;
      return;
    }

    for (const cls of classes) {
      const students = await api.getStudents(cls.Id || cls.id);
      const card = document.createElement("details");
      card.className = "card class-card";
      card.open = !collapsedClasses.has(cls.id);
      card.addEventListener("toggle", () => {
        if (card.open) collapsedClasses.delete(cls.id);
        else collapsedClasses.add(cls.id);
      });

      let studentsHtml = students.map((s, idx) => `
        <tr>
          <td style="width: 40px; color: var(--gray-500);">${idx + 1}</td>
          <td style="font-weight: 600;">${escapeHtml(s.fullName || s.FullName)}</td>
          <td style="text-align: right;">
            <button class="btn btn-secondary btn-sm delete-student-btn" data-class="${cls.id}" data-id="${s.id}" style="color: var(--danger);">&times;</button>
          </td>
        </tr>
      `).join("");

      card.innerHTML = `
        <summary>
          <span class="class-chevron" aria-hidden="true">›</span>
          <div>
            <h2 class="card-title">${escapeHtml(cls.name)}</h2>
            <p class="card-subtitle">Schulform: ${escapeHtml(cls.schulformName)} | ${students.length} Schüler</p>
          </div>
        </summary>
        <div class="class-body">
          <div class="class-actions">
            <button class="btn btn-primary btn-sm add-student-btn" data-class="${cls.id}">+ Schüler hinzufügen</button>
            <button class="btn btn-secondary btn-sm delete-class-btn" data-class="${cls.id}" style="color: var(--danger);">Klasse löschen</button>
          </div>

        <div class="table-responsive">
          <table class="data-table" style="font-size: 0.9rem;">
            <thead>
              <tr>
                <th>#</th>
                <th>Name</th>
                <th style="text-align: right;">Aktion</th>
              </tr>
            </thead>
            <tbody>
              ${studentsHtml || '<tr><td colspan="3" style="text-align: center; color: var(--gray-500);">Keine Schüler vorhanden.</td></tr>'}
            </tbody>
          </table>
        </div>
        </div>
      `;

      container.appendChild(card);
    }

    container.querySelectorAll(".add-student-btn").forEach(b => {
      b.addEventListener("click", () => openAddStudentModal(b.dataset.class));
    });
    container.querySelectorAll(".delete-student-btn").forEach(b => {
      b.addEventListener("click", async () => {
        if (confirm("Schüler wirklich löschen?")) {
          await api.deleteStudent(b.dataset.class, b.dataset.id);
          loadKlassen();
        }
      });
    });
    container.querySelectorAll(".delete-class-btn").forEach(b => {
      b.addEventListener("click", async () => {
        if (confirm("Klasse wirklich löschen oder archivieren?")) {
          await api.deleteClass(b.dataset.class);
          loadKlassen();
        }
      });
    });
  } catch (err) {
    showToast(err.message, "error");
  }
}

document.getElementById("newClassBtn").addEventListener("click", () => {
  if (state.schulformen.length === 0) {
    showToast("Bitte legen Sie zuerst mindestens eine Schulform an.", "error");
    return;
  }

  const modal = document.createElement("div");
  modal.className = "modal-backdrop";
  modal.innerHTML = `
    <div class="modal-content">
      <h2 class="modal-title">Neue Klasse anlegen</h2>
      <form id="newClassModalForm">
        <div class="form-group">
          <label class="form-label">Klassenbezeichnung</label>
          <input type="text" id="classNameInput" class="form-control" placeholder="z. B. 9b" required>
        </div>
        <div class="form-group">
          <label class="form-label">Zugehörige Schulform</label>
          <select id="classSfSelect" class="form-control">
            ${state.schulformen.map(s => `<option value="${s.id}">${escapeHtml(s.name)}</option>`).join("")}
          </select>
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 1.5rem;">
          <button type="button" class="btn btn-secondary" id="closeClassModalBtn">Abbrechen</button>
          <button type="submit" class="btn btn-primary">Anlegen</button>
        </div>
      </form>
    </div>
  `;
  document.getElementById("modalContainer").appendChild(modal);

  modal.querySelector("#closeClassModalBtn").addEventListener("click", () => modal.remove());
  modal.querySelector("#newClassModalForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const name = modal.querySelector("#classNameInput").value.trim();
    const sfId = modal.querySelector("#classSfSelect").value;
    const sf = state.schulformen.find(x => x.id === sfId);

    try {
      await api.saveClass({ name, schulformId: sfId, schulformName: sf ? sf.name : "" });
      showToast("Klasse erstellt.", "success");
      modal.remove();
      loadKlassen();
    } catch (err) {
      showToast(err.message, "error");
    }
  });
});

function openAddStudentModal(classId) {
  const modal = document.createElement("div");
  modal.className = "modal-backdrop";
  modal.innerHTML = `
    <div class="modal-content">
      <h2 class="modal-title">Schüler hinzufügen</h2>
      <p class="card-subtitle" style="margin-bottom: 1rem;">Nach dem Hinzufügen können Sie direkt den nächsten Namen eingeben.</p>
      <form id="addStudentModalForm">
        <div class="form-group">
          <label class="form-label" for="studentFirstName">Vorname</label>
          <input type="text" id="studentFirstName" class="form-control" placeholder="Max" required>
        </div>
        <div class="form-group">
          <label class="form-label" for="studentLastName">Nachname (optional)</label>
          <input type="text" id="studentLastName" class="form-control" placeholder="Mustermann">
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 1.5rem;">
          <button type="button" class="btn btn-secondary" id="closeStudentModalBtn">Fertig</button>
          <button type="submit" class="btn btn-primary">Hinzufügen & nächsten eingeben</button>
        </div>
      </form>
    </div>
  `;
  document.getElementById("modalContainer").appendChild(modal);
  modal.querySelector("#studentFirstName").focus();

  modal.querySelector("#closeStudentModalBtn").addEventListener("click", () => { modal.remove(); loadKlassen(); });
  modal.querySelector("#addStudentModalForm").addEventListener("submit", async (e) => {
    e.preventDefault();
    const lastName = modal.querySelector("#studentLastName").value.trim();
    const firstName = modal.querySelector("#studentFirstName").value.trim();
    if (!firstName) { showToast("Bitte einen Vornamen eingeben.", "error"); return; }
    const submitButton = modal.querySelector('button[type="submit"]');
    const closeButton = modal.querySelector("#closeStudentModalBtn");
    submitButton.disabled = true;
    closeButton.disabled = true;

    try {
      await api.saveStudent(classId, { lastName, firstName });
      showToast("Schüler hinzugefügt.", "success");
      modal.querySelector("#studentFirstName").value = "";
      modal.querySelector("#studentLastName").value = "";
      modal.querySelector("#studentFirstName").focus();
    } catch (err) {
      showToast(err.message, "error");
    } finally {
      submitButton.disabled = false;
      closeButton.disabled = false;
    }
  });
}

// ==========================================
// Klausur erstellen
// ==========================================
async function prepareNewExamForm() {
  const [classes, _] = await Promise.all([api.getClasses(), api.getSchulformen()]);
  state.classes = classes;

  const select = document.getElementById("newExamClass");
  select.innerHTML = classes.map(c => `<option value="${c.id}">${escapeHtml(c.name)} (${escapeHtml(c.schulformName)})</option>`).join("");

  document.getElementById("newExamDate").value = new Date().toISOString().split("T")[0];
  document.getElementById("newExamTitle").value = "";

  const tasksList = document.getElementById("newExamTasksList");
  tasksList.innerHTML = "";

  // 3 initial sample tasks
  addTaskRow("Aufgabe 1", 30);
  addTaskRow("Aufgabe 2", 30);
  addTaskRow("Aufgabe 3", 40);
  updateTotalExamPoints();
}

function addTaskRow(name = "", points = 10) {
  const container = document.getElementById("newExamTasksList");
  const taskNum = container.children.length + 1;
  const row = document.createElement("div");
  row.className = "task-row";
  row.style.cssText = "display: flex; gap: 0.75rem; align-items: center;";
  row.innerHTML = `
    <span style="font-weight: 700; width: 30px;">#${taskNum}</span>
    <input type="text" class="form-control task-name-input" placeholder="Aufgabenname" value="${escapeHtml(name || `Aufgabe ${taskNum}`)}" required style="flex: 2;">
    <div style="display: flex; align-items: center; gap: 0.25rem; flex: 1;">
      <input type="number" step="1" min="1" class="form-control task-pts-input" value="${points}" required style="width: 80px;">
      <span style="font-weight: 600; color: var(--gray-600);">Punkte</span>
    </div>
    <button type="button" class="btn btn-secondary btn-sm remove-task-btn" style="color: var(--danger);">&times;</button>
  `;

  row.querySelector(".task-pts-input").addEventListener("input", updateTotalExamPoints);
  row.querySelector(".remove-task-btn").addEventListener("click", () => {
    row.remove();
    updateTotalExamPoints();
  });

  container.appendChild(row);
}

document.getElementById("addTaskBtn").addEventListener("click", () => addTaskRow());
document.getElementById("cancelNewExamBtn").addEventListener("click", () => switchView("dashboard"));

function updateTotalExamPoints() {
  let total = 0;
  document.querySelectorAll(".task-pts-input").forEach(inp => {
    total += parseFloat(inp.value) || 0;
  });
  document.getElementById("newExamTotalPoints").textContent = `${formatNumber(total, 1)} / 100 P`;
}

document.getElementById("createExamForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const submitButton = e.currentTarget.querySelector('button[type="submit"]');
  if (submitButton.disabled) return;
  const classId = document.getElementById("newExamClass").value;
  const date = document.getElementById("newExamDate").value;
  const title = document.getElementById("newExamTitle").value.trim();

  const tasks = [];
  document.querySelectorAll("#newExamTasksList .task-row").forEach(row => {
    const name = row.querySelector(".task-name-input").value.trim();
    const maxPoints = parseFloat(row.querySelector(".task-pts-input").value) || 0;
    tasks.push({ name, maxPoints });
  });

  if (tasks.length === 0) {
    showToast("Mindestens eine Aufgabe erforderlich.", "error");
    return;
  }
  if (tasks.reduce((sum, task) => sum + task.maxPoints, 0) !== 100) {
    showToast("Die Höchstpunkte aller Aufgaben müssen zusammen genau 100 Punkte ergeben.", "error");
    document.querySelector("#newExamTasksList .task-pts-input")?.focus();
    return;
  }

  submitButton.disabled = true;
  try {
    const exam = await api.createExam(classId, title, date, tasks);
    showToast("Klausur angelegt!", "success");
    if (!state.isUnlocked || state.currentView !== "klausurNeu") return;
    state.examListView = "dashboard";
    await openExamMatrix(exam.id);
    if (state.isUnlocked && state.currentView === "eingabetabelle" && state.activeExam?.id === exam.id) {
      coverSheetOffer.dataset.examId = exam.id;
      coverSheetOffer.showModal();
    }
  } catch (err) {
    showToast(err.message, "error");
  } finally {
    submitButton.disabled = false;
  }
});

document.getElementById("skipCoverSheetBtn").addEventListener("click", () => coverSheetOffer.close());
document.getElementById("createCoverSheetNowBtn").addEventListener("click", () => {
  const examId = coverSheetOffer.dataset.examId;
  coverSheetOffer.close();
  if (state.isUnlocked && state.currentView === "eingabetabelle" && state.currentExamId === examId)
    coverSheetEditor.open(examId);
});
coverSheetOffer.addEventListener("cancel", e => { e.preventDefault(); coverSheetOffer.close(); });

// ==========================================
// Eingabetabelle (Score Matrix)
// ==========================================
async function openExamMatrix(examId, fromHistory = false) {
  state.currentExamId = examId;
  state.activeExam = null;
  state.activeExamEntries = [];
  document.getElementById("matrixTableContainer").replaceChildren();
  document.getElementById("matrixExamTitle").textContent = "Klausur wird geladen …";
  document.getElementById("matrixExamMeta").textContent = "";
  document.getElementById("matrixArchivedBadge").hidden = true;
  document.getElementById("matrixArchivedHint").hidden = true;
  document.getElementById("matrixKeyboardHint").hidden = true;
  document.getElementById("saveBadge").hidden = true;
  document.getElementById("matrixCoverSheetBtn").disabled = true;
  document.getElementById("matrixToAuswertungBtn").disabled = true;
  switchView("eingabetabelle", fromHistory ? "none" : "push");

  try {
    const exam = await api.getExam(examId);
    if (state.currentExamId !== examId || state.currentView !== "eingabetabelle") return;
    state.activeExam = exam;
    state.activeExamEntries = exam.studentEntries || [];

    document.getElementById("matrixExamTitle").textContent = exam.title;
    const archived = !!exam.isArchived;
    document.getElementById("matrixArchivedBadge").hidden = !archived;
    document.getElementById("matrixArchivedHint").hidden = !archived;
    document.getElementById("matrixKeyboardHint").hidden = archived;
    document.getElementById("saveBadge").hidden = archived;
    document.getElementById("matrixCoverSheetBtn").textContent = archived ? "Deckblatt ansehen" : "Deckblatt erstellen";
    document.getElementById("matrixCoverSheetBtn").disabled = false;
    document.getElementById("matrixToAuswertungBtn").disabled = false;
    document.getElementById("matrixExamMeta").textContent =
      `${exam.className} | ${exam.schulformName} | ${new Date(exam.date).toLocaleDateString("de-DE")} | Gesamt: ${formatNumber(exam.totalMaxPoints, 1)} Punkte`;
    document.getElementById("matrixStudentCount").textContent = `${state.activeExamEntries.length} Schüler`;

    renderMatrixTable(exam, state.activeExamEntries);
  } catch (err) {
    if (state.currentExamId === examId && state.currentView === "eingabetabelle")
      document.getElementById("matrixExamTitle").textContent = "Klausur konnte nicht geladen werden";
    showToast(err.message, "error");
  }
}

function renderMatrixTable(exam, entries) {
  const container = document.getElementById("matrixTableContainer");
  const totalMax = exam.totalMaxPoints;
  const profile = exam.gradingProfileSnapshot;
  const readOnly = exam.isArchived ? 'readonly aria-readonly="true"' : "";
  const disabled = exam.isArchived ? "disabled" : "";

  let tableHtml = `
    <table class="matrix-table" id="scoreMatrix">
      <thead>
        <tr>
          <th style="width: 35px;">#</th>
          <th class="sticky-col">Schüler/in</th>
  `;

  exam.tasks.forEach(t => {
    tableHtml += `<th>A${t.taskNumber}<br><span style="font-weight: 500; font-size: 0.8rem; color: var(--gray-500);">${formatNumber(t.maxPoints, 1)}P</span></th>`;
  });

  tableHtml += `
          <th class="calc-cell">Gesamt<br><span style="font-weight: 500; font-size: 0.8rem;">${formatNumber(totalMax, 1)}P</span></th>
          <th class="calc-cell">%</th>
          <th class="calc-cell">Ausgangsnote</th>
          <th>Wörter</th>
          <th>Fehler</th>
          <th class="calc-cell">FQ</th>
          <th class="calc-cell">Abzug</th>
          <th class="final-grade-cell">Endnote</th>
          <th style="min-width: 140px;">Status</th>
        </tr>
      </thead>
      <tbody>
  `;

  entries.forEach((entry, idx) => {
    tableHtml += `
      <tr data-student-id="${entry.studentId}" data-row-idx="${idx}">
        <td style="color: var(--gray-500);">${idx + 1}</td>
        <td class="sticky-col" title="${escapeHtml(entry.studentName)}">${escapeHtml(entry.studentName)}</td>
    `;

    exam.tasks.forEach(t => {
      const val = entry.taskPoints?.[t.taskNumber];
      const valStr = val !== null && val !== undefined ? String(val).replace(".", ",") : "";
      tableHtml += `
        <td>
          <input type="text" class="matrix-input task-pts-cell"
            data-task="${t.taskNumber}" data-max="${t.maxPoints}" data-legacy="${val !== null && val !== undefined && !Number.isInteger(val) ? val : ""}"
            value="${valStr}" placeholder="0" inputmode="numeric" ${readOnly}>
        </td>
      `;
    });

    tableHtml += `
        <td class="calc-cell row-total-pts">-</td>
        <td class="calc-cell row-pct">-</td>
        <td class="calc-cell row-init-grade">-</td>
        <td>
          <input type="number" class="matrix-input row-words-cell" style="width: 70px;"
            value="${entry.wordCount !== null && entry.wordCount !== undefined ? entry.wordCount : ''}" placeholder="Wörter" min="0" ${readOnly}>
        </td>
        <td>
          <input type="number" class="matrix-input row-errors-cell" style="width: 60px;"
            value="${entry.errorCount !== null && entry.errorCount !== undefined ? entry.errorCount : ''}" placeholder="Fehler" min="0" ${readOnly}>
        </td>
        <td class="calc-cell row-fq">-</td>
        <td class="calc-cell row-deduction">-</td>
        <td class="final-grade-cell row-final-grade">-</td>
        <td>
          <select class="matrix-select row-status-select" ${disabled}>
            <option value="1" ${entry.status === 1 || entry.status === 4 ? 'selected' : ''}>Teilgenommen</option>
            <option value="3" ${entry.status === 2 || entry.status === 3 ? 'selected' : ''}>Nicht teilgenommen</option>
          </select>
        </td>
      </tr>
    `;
  });

  tableHtml += `</tbody></table>`;
  container.innerHTML = tableHtml;

  // Bind input listeners for live calculation and autosave
  const table = container.querySelector("#scoreMatrix");

  table.querySelectorAll("tbody tr").forEach((tr, rowIdx) => {
    recalculateRow(tr, exam, entries[rowIdx]);

    if (exam.isArchived) return;
    tr.querySelectorAll(".matrix-input, .row-status-select").forEach(input => {
      input.addEventListener("input", () => {
        if (!collectRowData(tr, exam, entries[rowIdx])) {
          state.saveRevision = (state.saveRevision || 0) + 1;
          state.saveFailed = true;
          document.getElementById("saveBadge").textContent = "Eingabe prüfen";
          return;
        }
        recalculateRow(tr, exam, entries[rowIdx], true);
        triggerAutoSave();
      });

      // Handle Enter/Tab navigation
      input.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          e.preventDefault();
          // Move to next cell down or right
          focusNextMatrixCell(input, tr);
        }
      });
    });
  });
}

function collectRowData(tr, exam, entry) {
  const taskPoints = {};
  let valid = true;
  tr.querySelectorAll(".task-pts-cell").forEach(inp => {
    const taskNum = parseInt(inp.dataset.task, 10);
    const maxPts = parseFloat(inp.dataset.max);
    const val = parseGermanNumber(inp.value);
    const legacy = inp.dataset.legacy !== "" && val === Number(inp.dataset.legacy);
    const invalid = inp.value.trim() !== "" && (val === null || val < 0 || val > maxPts || (!Number.isInteger(val) && !legacy));
    inp.setCustomValidity(invalid ? `Bitte ganze Punkte zwischen 0 und ${maxPts} eingeben.` : "");
    inp.setAttribute("aria-invalid", String(invalid));
    valid = valid && !invalid;
    taskPoints[taskNum] = val;
  });

  const wordsInput = tr.querySelector(".row-words-cell");
  const errorsInput = tr.querySelector(".row-errors-cell");
  for (const inp of [wordsInput, errorsInput]) {
    const invalid = inp.value !== "" && (!Number.isInteger(Number(inp.value)) || Number(inp.value) < 0);
    inp.setCustomValidity(invalid ? "Bitte eine nichtnegative ganze Zahl eingeben." : "");
    inp.setAttribute("aria-invalid", String(invalid));
    valid = valid && !invalid;
  }
  if (!valid) return false;
  entry.taskPoints = taskPoints;
  entry.wordCount = wordsInput.value !== "" ? Number(wordsInput.value) : null;
  entry.errorCount = errorsInput.value !== "" ? Number(errorsInput.value) : null;
  entry.status = parseInt(tr.querySelector(".row-status-select").value, 10);
  return true;
}

async function recalculateRow(tr, exam, entry, fromUserInput = false) {
  const revision = (Number(tr.dataset.revision) || 0) + 1;
  tr.dataset.revision = revision;
  try {
    // The same calculator is used by the summary and exports.
    const result = await api.previewExamEntry(exam.id, entry);
    if (Number(tr.dataset.revision) !== revision || !tr.isConnected) return;
    const maximumDeduction = result.isMaximumDeduction === true;
    if (fromUserInput && !exam.isArchived && state.isUnlocked && state.currentView === "eingabetabelle" &&
        state.currentExamId === exam.id && maximumDeduction && tr.dataset.maximumDeduction !== "true") showHamster();
    tr.dataset.maximumDeduction = String(maximumDeduction);
    tr.querySelector(".row-total-pts").textContent = result.adjustedPoints == null ? formatNumber(result.rawPoints, 1) :
      `${formatNumber(result.rawPoints, 1)} → ${formatNumber(result.adjustedPoints, 2)}`;
    tr.querySelector(".row-pct").textContent = result.percentage == null ? "-" : `${formatNumber(result.percentage, 1)}%`;
    tr.querySelector(".row-init-grade").textContent = result.initialGrade ?? "-";
    tr.querySelector(".row-fq").textContent = formatNumber(result.errorQuotient, 2);
    tr.querySelector(".row-deduction").textContent = result.finalGrade == null ? "-" : result.deductionPoints == null ?
      formatNumber(result.deduction, 1) : `${formatNumber(result.deduction, 1)}% (${formatNumber(result.deductionPoints, 2)} P)`;
    tr.querySelector(".row-final-grade").textContent = result.finalGrade ?? "-";
  } catch (err) {
    if (Number(tr.dataset.revision) === revision) tr.querySelector(".row-final-grade").textContent = "-";
  }
}

function focusNextMatrixCell(currentInput, currentTr) {
  const currentTd = currentInput.closest("td");
  const cellIndex = currentTd.cellIndex;
  const nextTr = currentTr.nextElementSibling;
  if (nextTr) {
    const nextCell = nextTr.cells[cellIndex]?.querySelector("input, select");
    if (nextCell) {
      nextCell.focus();
      if (nextCell.select) nextCell.select();
    }
  }
}

function triggerAutoSave() {
  const badge = document.getElementById("saveBadge");
  badge.className = "save-badge saving";
  badge.innerHTML = `<span style="width: 8px; height: 8px; border-radius: 50%; background-color: currentColor;"></span> Speichert...`;

  if (state.saveAction && state.pendingExamId !== state.currentExamId) state.saveAction();
  clearTimeout(state.saveTimeout);
  const examId = state.currentExamId;
  const entries = structuredClone(state.activeExamEntries);
  const revision = state.saveRevision = (state.saveRevision || 0) + 1;
  state.pendingExamId = examId;
  const save = () => {
    if (state.saveAction === save) state.saveAction = null;
    clearTimeout(state.saveTimeout);
    state.isSaving = true;
    state.pendingSave = (state.pendingSave || Promise.resolve()).catch(() => {}).then(() => api.saveExamEntries(examId, entries));
    state.pendingSave.then(() => {
      if (revision !== state.saveRevision) return;
      state.isSaving = false;
      state.saveFailed = false;
      if (examId !== state.currentExamId || state.saveAction) return;
      badge.className = "save-badge saved";
      badge.innerHTML = `<span style="width: 8px; height: 8px; border-radius: 50%; background-color: currentColor;"></span> Gespeichert`;
    }).catch(err => {
      if (revision === state.saveRevision) state.isSaving = false;
      state.saveFailed = true;
      badge.className = "save-badge saving";
      badge.innerHTML = `Fehler beim Speichern`;
      showToast(err.message, "error");
    });
    return state.pendingSave;
  };
  state.saveAction = save;
  state.saveTimeout = setTimeout(save, 500);
}

async function flushPendingSave() {
  await coverSheetEditor.flush();
  if (document.querySelector('#scoreMatrix [aria-invalid="true"]'))
    throw new Error("Bitte ungültige Eingaben korrigieren.");
  if (state.saveAction) state.saveAction();
  if (state.pendingSave) await state.pendingSave;
}

window.addEventListener("beforeunload", e => {
  if (state.saveAction || state.isSaving || state.saveFailed || document.querySelector('#scoreMatrix [aria-invalid="true"]')) { e.preventDefault(); e.returnValue = ""; }
});

document.getElementById("matrixBackBtn").addEventListener("click", () => switchView(state.examListView));
document.getElementById("matrixToAuswertungBtn").addEventListener("click", () => openExamAuswertung(state.currentExamId));

// ==========================================
// Auswertung & Notenspiegel
// ==========================================
async function openExamAuswertung(examId, fromHistory = false) {
  try { await flushPendingSave(); }
  catch { showToast("Die Eingaben konnten nicht gespeichert werden.", "error"); if (fromHistory) switchView(state.currentView, "push"); return; }
  state.currentExamId = examId;
  state.activeExam = null;
  document.getElementById("auswertungExamTitle").textContent = "Auswertung wird geladen …";
  document.getElementById("auswertungExamMeta").textContent = "";
  document.getElementById("auswertungArchivedBadge").hidden = true;
  document.getElementById("auswertungCoverSheetBtn").disabled = true;
  document.getElementById("recalculateBtn").disabled = true;
  document.getElementById("exportPdfBtn").disabled = true;
  document.getElementById("exportDocxBtn").disabled = true;
  switchView("auswertung", fromHistory ? "none" : "push");

  try {
    const [exam, summary] = await Promise.all([
      api.getExam(examId),
      api.getExamSummary(examId)
    ]);
    if (state.currentExamId !== examId || state.currentView !== "auswertung") return;
    state.activeExam = exam;

    document.getElementById("auswertungExamTitle").textContent = `Auswertung: ${exam.title}`;
    document.getElementById("auswertungArchivedBadge").hidden = !exam.isArchived;
    document.getElementById("recalculateBtn").disabled = !!exam.isArchived;
    document.getElementById("recalculateBtn").title = exam.isArchived ? "Zum Bearbeiten die Klausur im Archiv wiederherstellen." : "";
    document.getElementById("auswertungCoverSheetBtn").disabled = false;
    document.getElementById("auswertungCoverSheetBtn").textContent = exam.isArchived ? "Deckblatt ansehen" : "Deckblatt erstellen";
    document.getElementById("exportPdfBtn").disabled = false;
    document.getElementById("exportDocxBtn").disabled = false;
    document.getElementById("auswertungExamMeta").textContent =
      `Klasse ${exam.className} | Schulform: ${exam.schulformName} | Datum: ${new Date(exam.date).toLocaleDateString("de-DE")}`;

    document.getElementById("auswertungAverage").textContent =
      summary.classAverage !== null ? formatNumber(summary.classAverage, 2) : "-";
    document.getElementById("auswertungAverageUnit").textContent = summary.unit;

    document.getElementById("auswertungGradedCount").textContent = summary.gradedCount;
    document.getElementById("auswertungTotalCount").textContent = `von ${summary.totalStudents} Schülern`;
    document.getElementById("auswertungExcludedCount").textContent = summary.excludedCount;

    // Render Notenspiegel Chart & Table
    renderNotenspiegel(summary.gradeDistribution, summary.unit);

    // Render Rules Summary
    const sfProf = exam.gradingProfileSnapshot;
    const rulesEl = document.getElementById("auswertungRulesSummary");
    rulesEl.innerHTML = `
      <ul style="padding-left: 1.25rem;">
        <li>Notensystem: <strong>${sfProf.system === 1 ? 'Schulnoten (1-6)' : 'Notenpunkte (0-15)'}</strong></li>
        <li>Abzugseinheit: <strong>${sfProf.deductionUnit === 1 ? 'Schulnoten' : sfProf.deductionUnit === 2 ? 'Notenpunkte' : '% der Gesamtpunkte'}</strong></li>
        <li>Abzugsstufen: ${sfProf.deductionTiers.map(t => `ab FQ ${formatNumber(t.threshold, 1)}: -${formatNumber(t.deduction, 1)}`).join(", ") || "Keine"}</li>
      </ul>
    `;
  } catch (err) {
    if (state.currentExamId === examId && state.currentView === "auswertung")
      document.getElementById("auswertungExamTitle").textContent = "Auswertung konnte nicht geladen werden";
    showToast(err.message, "error");
  }
}

function renderNotenspiegel(dist, unit) {
  const chartContainer = document.getElementById("notenspiegelChart");
  const theadTr = document.getElementById("notenspiegelTableHeader");
  const tbodyTr = document.getElementById("notenspiegelTableBody");

  chartContainer.innerHTML = "";
  theadTr.innerHTML = "";
  tbodyTr.innerHTML = "";

  const entries = Object.entries(dist);
  const maxCount = Math.max(...entries.map(([_, count]) => count), 1);

  entries.forEach(([gradeLabel, count]) => {
    // Chart group
    const group = document.createElement("div");
    group.className = "chart-bar-group";

    const heightPct = (count / maxCount) * 160;

    group.innerHTML = `
      <span class="chart-bar-count">${count}</span>
      <div class="chart-bar" style="height: ${heightPct}px;"></div>
      <span class="chart-bar-label">${gradeLabel}</span>
    `;
    chartContainer.appendChild(group);

    // Table Header & Body
    const th = document.createElement("th");
    th.textContent = gradeLabel;
    theadTr.appendChild(th);

    const td = document.createElement("td");
    td.textContent = count;
    td.style.fontWeight = "700";
    tbodyTr.appendChild(td);
  });
}

// PDF & DOCX Export Handlers
document.getElementById("exportPdfBtn").addEventListener("click", () => {
  if (!state.currentExamId) return;
  window.open(`/api/klausuren/${state.currentExamId}/export/pdf`, "_blank");
});

document.getElementById("exportDocxBtn").addEventListener("click", () => {
  if (!state.currentExamId) return;
  window.open(`/api/klausuren/${state.currentExamId}/export/docx`, "_blank");
});

document.getElementById("auswertungToMatrixBtn").addEventListener("click", () => {
  openExamMatrix(state.currentExamId);
});

document.getElementById("recalculateBtn").addEventListener("click", async () => {
  if (!confirm("Klausur wirklich mit den aktuellen Schulform-Regeln neu berechnen?")) return;
  try {
    await api.recalculateExam(state.currentExamId);
    showToast("Klausur erfolgreich neu berechnet.", "success");
    openExamAuswertung(state.currentExamId);
  } catch (err) {
    showToast(err.message, "error");
  }
});

// ==========================================
// Einstellungen, Sicherung & Danger Zone
// ==========================================
async function loadSettings() {
  try {
    const status = await api.getSetupStatus();
    updateHelloControls(status);
    document.getElementById("settingsTimeout").value = status.inactivityTimeoutMinutes.toString();
    loadBackupsTable();
  } catch (err) {
    showToast(err.message, "error");
  }
}

async function loadBackupsTable() {
  try {
    const automatic = await api.getAutomaticBackupStatus();
    document.getElementById("automaticBackupEnabled").checked = automatic.enabled;
    document.getElementById("automaticBackupInterval").value = String(automatic.intervalHours);
    document.getElementById("automaticBackupStatus").textContent = automatic.error ||
      `${automatic.enabled ? "Automatische Sicherung ist eingeschaltet." : "Automatische Sicherung ist ausgeschaltet."} ${automatic.lastBackup ? `Letzte automatische Sicherung: ${new Date(automatic.lastBackup).toLocaleString("de-DE")}.` : "Noch keine automatische Sicherung vorhanden."}`;
    const backups = await api.getBackups();
    const tbody = document.getElementById("backupsListTable");
    tbody.innerHTML = "";

    if (backups.length === 0) {
      tbody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--gray-500); padding: 1.5rem;">Keine lokalen Sicherungen vorhanden.</td></tr>`;
      return;
    }

    backups.forEach(b => {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td style="font-weight: 600;">${escapeHtml(b.fileName)}</td>
        <td>${new Date(b.createdAt).toLocaleString("de-DE")}</td>
        <td>${(b.fileSizeBytes / 1024).toFixed(1)} KB</td>
        <td class="backup-action-cell"><div class="backup-row-actions">
          <button class="btn btn-secondary btn-sm restore-backup-btn" data-file="${escapeHtml(b.fileName)}">Wiederherstellen</button>
          <button class="btn btn-secondary btn-sm delete-backup-btn" data-file="${escapeHtml(b.fileName)}" aria-label="Sicherung ${escapeHtml(b.fileName)} löschen" title="Sicherung löschen">&times;</button>
        </div>
        </td>
      `;
      tbody.appendChild(tr);
    });

    tbody.querySelectorAll(".restore-backup-btn").forEach(b => {
      b.addEventListener("click", () => openRestoreBackupModal(b.dataset.file));
    });
    tbody.querySelectorAll(".delete-backup-btn").forEach(b => {
      b.addEventListener("click", async () => {
        if (confirm(`Sicherung '${b.dataset.file}' wirklich löschen?`)) {
          await api.deleteBackup(b.dataset.file);
          loadBackupsTable();
        }
      });
    });
  } catch (err) {
    showToast(err.message, "error");
  }
}

document.getElementById("automaticBackupForm").addEventListener("submit", async e => {
  e.preventDefault();
  const button = e.currentTarget.querySelector('button[type="submit"]');
  button.disabled = true;
  try {
    await api.setAutomaticBackupSettings(document.getElementById("automaticBackupEnabled").checked,
      Number(document.getElementById("automaticBackupInterval").value));
    await loadBackupsTable();
    showToast("Sicherungseinstellungen gespeichert.", "success");
  } catch (err) { showToast(err.message, "error"); }
  finally { button.disabled = false; }
});

document.getElementById("changePinForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const oldPin = document.getElementById("oldPin").value;
  const newPin = document.getElementById("newPin").value;

  try {
    await api.changePin(oldPin, newPin);
    showToast("App-PIN erfolgreich geändert.", "success");
    document.getElementById("changePinForm").reset();
  } catch (err) {
    showToast(err.message, "error");
  }
});

document.getElementById("checkHelloSupportBtn").addEventListener("click", async () => {
  const result = document.getElementById("helloSupportResult");
  if (!window.isSecureContext || !window.PublicKeyCredential ||
      typeof PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable !== "function") {
    result.textContent = "Dieser Browser stellt die benötigte Geräteanmeldung nicht bereit. Die Prüfung aktiviert Windows Hello nicht.";
    return;
  }
  try {
    const available = await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
    result.textContent = available
      ? "Eine Geräteanmeldung ist verfügbar. Ob Windows Hello und der benötigte Schlüsselschutz unterstützt werden, muss noch geprüft werden. Die Funktion ist nicht aktiviert."
      : "Keine bestätigende Geräteanmeldung verfügbar. Prüfen Sie Windows Hello und die Browser-Einstellungen. Die Funktion ist nicht aktiviert.";
  } catch {
    result.textContent = "Die Geräteunterstützung konnte nicht geprüft werden. Die Funktion ist nicht aktiviert.";
  }
});

document.getElementById("registerHelloBtn").addEventListener("click", async () => {
  const pinInput = document.getElementById("helloPin");
  const pin = pinInput.value;
  pinInput.value = "";
  if (!pin) { showToast("Bitte die App-PIN eingeben.", "error"); return; }
  try {
    await registerHello(pin);
    showToast("Geräteanmeldung eingerichtet.", "success");
    updateHelloControls(await api.getSetupStatus());
  } catch (err) {
    showToast(err.message, "error");
  }
});

document.getElementById("removeHelloBtn").addEventListener("click", async () => {
  const pinInput = document.getElementById("helloPin");
  const pin = pinInput.value;
  pinInput.value = "";
  if (!pin) { showToast("Bitte die App-PIN eingeben.", "error"); return; }
  try {
    await api.removeHello(pin);
    showToast("Windows Hello Registrierung entfernt.", "info");
    updateHelloControls(await api.getSetupStatus());
  } catch (err) {
    showToast(err.message, "error");
  }
});

document.getElementById("saveTimeoutBtn").addEventListener("click", async () => {
  const timeout = parseInt(document.getElementById("settingsTimeout").value, 10);
  try {
    await api.updateTimeout(timeout);
    showToast("Sperrzeitraum aktualisiert.", "success");
  } catch (err) {
    showToast(err.message, "error");
  }
});

document.getElementById("openCreateBackupModalBtn").addEventListener("click", async e => {
    const button = e.currentTarget;
    button.disabled = true;
    try {
      await api.createBackup();
      showToast("Verschlüsselte Sicherung erfolgreich erstellt.", "success");
      loadBackupsTable();
    } catch (err) {
      showToast(err.message, "error");
    } finally { button.disabled = false; }
});

document.getElementById("openDownloadBackupModalBtn").addEventListener("click", async e => {
    const button = e.currentTarget;
    button.disabled = true;
    try {
      const res = await api.downloadBackup();
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `klausurfix_backup_${new Date().toISOString().replace(/[:.]/g, "-")}.kfbak`;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      showToast(err.message, "error");
    } finally { button.disabled = false; }
});

function openRestoreBackupModal(fileName) {
  if (!confirm("Die aktuellen Daten werden durch die Sicherung ersetzt. Fortfahren?")) return;
  openPromptModal(`Sicherung wiederherstellen: ${fileName}`, "Geben Sie den Wiederherstellungsschlüssel für diese Sicherung ein:", async (key) => {
    try {
      await api.restoreBackup(fileName, key);
      showToast("Sicherung erfolgreich wiederhergestellt!", "success");
      switchView("dashboard");
    } catch (err) {
      showToast(err.message, "error");
    }
  });
}

document.getElementById("restoreFileBtn").addEventListener("click", () => document.getElementById("restoreFileInput").click());
document.getElementById("restoreFileInput").addEventListener("change", async e => {
  const file = e.target.files[0];
  e.target.value = "";
  if (!file || !confirm("Die aktuellen Daten werden durch die Sicherung ersetzt. Fortfahren?")) return;
  if (file.size > 18_000_000) { showToast("Die Sicherungsdatei ist zu groß.", "error"); return; }
  const reader = new FileReader();
  reader.onerror = () => showToast("Die Sicherungsdatei konnte nicht gelesen werden.", "error");
  reader.onload = () => openPromptModal("Sicherung wiederherstellen", "Wiederherstellungsschlüssel dieser Sicherung:", async key => {
    try {
      await api.restoreBackupFile(String(reader.result).split(",", 2)[1], key);
      showToast("Sicherung erfolgreich wiederhergestellt.", "success");
      switchView("dashboard");
    } catch (err) { showToast(err.message, "error"); }
  });
  reader.readAsDataURL(file);
});

// Danger Zone Actions
document.getElementById("dangerClearDataBtn").addEventListener("click", () => {
  openPinConfirmModal("Anwendungsdaten löschen", "Möchten Sie wirklich alle Klassen, Klausuren und Schülernoten löschen? Sicherungen bleiben erhalten.", async (pin) => {
    try {
      await api.dangerClearActiveData(pin);
      showToast("Aktive Daten gelöscht.", "success");
      switchView("dashboard");
    } catch (err) {
      showToast(err.message, "error");
    }
  });
});

document.getElementById("dangerDeleteBackupsBtn").addEventListener("click", () => {
  openPinConfirmModal("Alle Sicherungen löschen", "Möchten Sie wirklich alle lokalen Sicherungsdateien unwiderruflich löschen?", async (pin) => {
    try {
      await api.dangerDeleteAllBackups(pin);
      showToast("Alle lokalen Sicherungen gelöscht.", "success");
      loadBackupsTable();
    } catch (err) {
      showToast(err.message, "error");
    }
  });
});

document.getElementById("dangerFullResetBtn").addEventListener("click", () => {
  openPinConfirmModal("Vollständiges Zurücksetzen (Factory Reset)", "ACHTUNG: Dies löscht die aktive Datenbank, alle lokalen Sicherungen und Sicherheitsschlüssel! Die App kehrt zum Einrichtungszustand zurück.", async (pin) => {
    try {
      await api.dangerFullReset(pin);
      showToast("App wurde vollständig zurückgesetzt.", "info");
      window.location.reload();
    } catch (err) {
      showToast(err.message, "error");
    }
  });
});

// Helper Modals
function openPromptModal(title, text, onConfirm) {
  const modal = document.createElement("div");
  modal.className = "modal-backdrop";
  modal.innerHTML = `
    <div class="modal-content">
      <h3 class="modal-title">${escapeHtml(title)}</h3>
      <p style="font-size: 0.95rem; color: var(--gray-600); margin-bottom: 1rem;">${escapeHtml(text)}</p>
      <input type="text" id="promptInput" class="form-control" style="font-family: monospace; font-weight: 700;" placeholder="KF-XXXX-XXXX-XXXX-XXXX">
      <div style="display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 1.5rem;">
        <button type="button" class="btn btn-secondary" id="promptCancelBtn">Abbrechen</button>
        <button type="button" class="btn btn-primary" id="promptSubmitBtn">Bestätigen</button>
      </div>
    </div>
  `;
  document.getElementById("modalContainer").appendChild(modal);

  modal.querySelector("#promptCancelBtn").addEventListener("click", () => modal.remove());
  modal.querySelector("#promptSubmitBtn").addEventListener("click", async () => {
    const val = modal.querySelector("#promptInput").value.trim();
    if (!val) return;
    modal.remove();
    await onConfirm(val);
  });
}

function openPinConfirmModal(title, warningText, onConfirm) {
  const modal = document.createElement("div");
  modal.className = "modal-backdrop";
  modal.innerHTML = `
    <div class="modal-content">
      <h3 class="modal-title" style="color: var(--danger);">${escapeHtml(title)}</h3>
      <p style="font-size: 0.95rem; color: var(--gray-700); margin-bottom: 1.25rem;">${escapeHtml(warningText)}</p>
      <div class="form-group">
        <label class="form-label">Zur Bestätigung Ihre App-PIN eingeben:</label>
        <input type="password" id="dangerPinInput" class="form-control" inputmode="numeric" required placeholder="PIN eingeben">
      </div>
      <div style="display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 1.5rem;">
        <button type="button" class="btn btn-secondary" id="dangerCancelBtn">Abbrechen</button>
        <button type="button" class="btn btn-danger" id="dangerSubmitBtn">Ausführen</button>
      </div>
    </div>
  `;
  document.getElementById("modalContainer").appendChild(modal);

  modal.querySelector("#dangerCancelBtn").addEventListener("click", () => modal.remove());
  modal.querySelector("#dangerSubmitBtn").addEventListener("click", async () => {
    const pin = modal.querySelector("#dangerPinInput").value;
    if (!pin) return;
    modal.remove();
    await onConfirm(pin);
  });
}

function escapeHtml(str) {
  if (!str) return "";
  return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}

// Start application
init();

