const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
}[c]));

export function createCoverSheetEditor({ api, notify, beforeOpen }) {
  let active = null;
  let opening = false;
  let sessionEpoch = 0;

  function clear() {
    if (!active) return;
    const current = active;
    active = null;
    clearTimeout(current.timer);
    current.toolbarObserver?.disconnect();
    current.dialog.close();
    current.dialog.remove();
    current.model = null;
  }
  window.addEventListener("klausurfix:locked", () => { sessionEpoch++; clear(); });
  window.addEventListener("beforeunload", e => {
    if (active?.dirty || active?.saving) { e.preventDefault(); e.returnValue = ""; }
  });

  function read(current) {
    const form = current.dialog.querySelector("form");
    const selectedOperators = new Set([...form.querySelectorAll('input[name="operator"]:checked')].map(i => i.value));
    return {
      subject: form.elements.subject.value.trim(), instructions: form.elements.instructions.value.trim(),
      useTemplateLogo: form.elements.useTemplateLogo.checked, showGradeKey: form.elements.showGradeKey.checked,
      showDeductionRules: form.elements.showDeductionRules.checked, showWordCount: form.elements.showWordCount.checked,
      showErrorCount: form.elements.showErrorCount.checked, showErrorQuotient: form.elements.showErrorQuotient.checked,
      showGradeDeduction: form.elements.showGradeDeduction.checked,
      deductionNotice: form.elements.deductionNotice.value.trim() === current.model.defaultDeductionNotice
        ? null : form.elements.deductionNotice.value.trim(),
      saveDeductionNotice: true,
      tasks: [...form.querySelectorAll("[data-cover-task]")].map(t => ({ taskNumber: Number(t.dataset.coverTask), text: t.value.trim() })),
      operatorIds: current.operatorOrder.filter(id => selectedOperators.has(id))
    };
  }

  function status(current, text, saved = false) {
    const badge = current.dialog.querySelector("[data-save-status]");
    badge.textContent = text;
    badge.className = `save-badge ${saved ? "saved" : "saving"}`;
  }

  async function flush({ quiet = false } = {}) {
    const current = active;
    if (!current) return;
    if (current.readOnly) return;
    clearTimeout(current.timer);
    if (current.saving) return current.saving;
    if (!current.dirty) return;
    const form = current.dialog.querySelector("form");
    if (!form.checkValidity()) {
      if (!quiet) form.reportValidity();
      throw new Error("Bitte die Deckblatteingaben prüfen.");
    }
    current.saving = (async () => {
      while (current === active && current.dirty) {
        if (!form.checkValidity()) throw new Error("Bitte die Deckblatteingaben prüfen.");
        const revision = current.revision;
        const data = read(current);
        status(current, "Speichert …");
        const model = await api.saveCoverSheet(current.examId, data);
        if (current !== active) return;
        current.model = model;
        if (revision === current.revision) {
          current.dirty = false;
          status(current, "Gespeichert", true);
          current.dialog.querySelector("[data-error]").textContent = "";
          renderPreview(current);
        }
      }
    })();
    try { await current.saving; }
    catch (err) {
      if (current === active) {
        status(current, "Nicht gespeichert");
        current.dialog.querySelector("[data-error]").textContent = `${err.message} Speichern wird automatisch erneut versucht.`;
        clearTimeout(current.timer);
        current.timer = setTimeout(() => flush({ quiet: true }).catch(() => {}), 5000);
      }
      throw err;
    } finally { current.saving = null; }
  }

  function renderPreview(current) {
    const m = current.model, s = m.settings;
    const preview = current.dialog.querySelector("[data-preview]");
    preview.innerHTML = `
      <article class="cover-paper" aria-label="Deckblattvorschau">
        <header class="cover-paper-header">
          ${s.useTemplateLogo ? '<img src="/api/deckblatt/logo" alt="Saalburgschule Usingen">' : ""}
          <span>Name:___________________</span><div class="cover-header-row"><span>${escape(m.headerTitle)}</span><span>${escape(m.headerClass)}</span><span>${escape(m.headerDate)}</span></div></header>
        ${s.showGradeKey ? `<div class="cover-key-scroll"><table class="cover-grade-key"><tbody>
          <tr><th>Punkte</th>${m.gradeKey.map(g => `<td>${escape(g.percentage)}</td>`).join("")}</tr>
          <tr><th>${escape(m.gradeUnit)}</th>${m.gradeKey.map(g => `<td>${escape(g.grade)}</td>`).join("")}</tr>
        </tbody></table></div>` : ""}
        <p class="cover-center cover-rules" data-notice-preview ${s.showDeductionRules && m.deductionNotice ? "" : "hidden"}>${escape(m.deductionNotice)}</p>
        ${s.instructions ? `<p class="cover-text cover-center">${escape(s.instructions)}</p>` : ""}
        ${s.showWordCount ? '<p class="cover-center"><strong>Zählen Sie bitte am Ende der Klausur Ihre geschriebenen Wörter!</strong></p><p class="cover-center"><strong>Anzahl Wörter: ____________________</strong></p>' : ""}
        ${m.additionalFields.length ? `<p class="cover-additional-fields"><strong>${m.additionalFields.map(escape).join("    ")}</strong></p>` : ""}
        ${m.tasks.map(t => `<section class="cover-task-preview"><strong>${escape(t.title)}</strong>
          <p class="cover-text">${t.text ? t.textParts.map(p => p.underlined ? `<u>${escape(p.text)}</u>` : escape(p.text)).join("") : "Aufgabenstellung: ______________________________________________"}</p><p class="cover-points">${escape(t.shortPoints)}</p></section>`).join("")}
        <section class="cover-paper-footer">${s.operators.map(o => `<p><strong>Operator „${escape(o.name)}“:</strong><br>${escape(o.definition)}</p>`).join("")}</section>
      </article>`;
  }

  async function download(current, format) {
    const button = current.dialog.querySelector(`[data-download="${format}"]`);
    button.disabled = true;
    try {
      await flush();
      if (current !== active) return;
      const response = await fetch(`/api/klausuren/${encodeURIComponent(current.examId)}/deckblatt/${format}`, { cache: "no-store" });
      if (response.status === 401) window.dispatchEvent(new CustomEvent("klausurfix:locked"));
      if (!response.ok) throw new Error("Das Deckblatt konnte nicht exportiert werden.");
      const blob = await response.blob();
      if (current !== active) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      let filename = `Deckblatt.${format}`;
      if (format === "docx") {
        const encoded = /(?:^|;)\s*filename\*\s*=\s*UTF-8''([^;]+)/i.exec(response.headers.get("Content-Disposition") || "")?.[1];
        if (encoded) {
          try { filename = decodeURIComponent(encoded); } catch { /* Use the fallback if the header is malformed. */ }
        }
      }
      link.href = url; link.download = filename;
      document.body.appendChild(link); link.click(); link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (err) { notify(err.message, "error"); }
    finally { button.disabled = false; }
  }

  async function open(examId, { readOnly: requestedReadOnly = false } = {}) {
    if (!examId || active || opening) return;
    opening = true;
    const epoch = sessionEpoch;
    try {
      await beforeOpen();
      const [model, catalog] = await Promise.all([api.getCoverSheet(examId), api.getOperators()]);
      if (epoch !== sessionEpoch) return;
      const readOnly = requestedReadOnly || !!model.isArchived;
      const dialog = document.createElement("dialog");
      dialog.className = "cover-dialog";
      dialog.setAttribute("aria-labelledby", "coverDialogTitle");
      const s = model.settings;
      const selected = new Set(s.operators.map(o => o.id));
      // Historical definitions take precedence over the current catalog.
      const operators = [...new Map([...catalog.operators, ...s.operators].map(o => [o.id, o])).values()]
        .sort((a, b) => a.name.localeCompare(b.name, "de"));
      dialog.innerHTML = `
        <header class="cover-toolbar"><div><h2 id="coverDialogTitle">${readOnly ? "Deckblatt ansehen" : "Deckblatt erstellen"}</h2>
          <p class="card-subtitle">${escape(model.title)}</p></div>
          <span class="${readOnly ? "archive-badge" : `save-badge ${model.isSaved ? "saved" : "saving"}`}" data-save-status role="status">${readOnly ? "Archiviert · schreibgeschützt" : model.isSaved ? "Gespeichert" : "Vorlage – noch nicht gespeichert"}</span>
          <div class="cover-actions"><button type="button" class="btn btn-primary" data-download="pdf">PDF herunterladen</button>
            <button type="button" class="btn btn-secondary" data-download="docx">DOCX herunterladen</button>
            <button type="button" class="btn btn-secondary" data-close>Schließen</button></div></header>
        <div class="cover-editor-layout"><form class="cover-editor-form">
          ${readOnly ? '<p class="cover-help">Zum Bearbeiten die Klausur im Archiv wiederherstellen.</p>' : ""}
          <div class="form-group"><label class="form-label" for="coverSubject">Zusatz zur Kopfzeile (optional)</label>
            <input class="form-control" id="coverSubject" name="subject" maxlength="200" value="${escape(s.subject)}">
            <p class="cover-help">Der Klausurtitel wird automatisch übernommen.</p></div>
          <fieldset class="cover-options"><legend>Inhalt des Deckblatts</legend>
            ${[["useTemplateLogo", "Logo der Saalburgschule aus der Vorlage"], ["showGradeKey", "Notenschlüssel der Klausur"],
              ["showDeductionRules", "Hinweis zum Fehlerabzug"], ["showWordCount", "Hinweis und Feld zur Wortanzahl"], ["showErrorCount", "Feld für Anzahl Fehler"],
              ["showErrorQuotient", "Feld für Fehlerquotient"], ["showGradeDeduction", "Feld für Abzug"]]
              .map(([key, label]) => `<label><input type="checkbox" name="${key}" ${s[key] ? "checked" : ""}> ${label}</label>`).join("")}
          </fieldset>
          <div class="form-group"><label class="form-label" for="coverDeductionNotice">Hinweis zum Fehlerabzug</label>
            <textarea class="form-control" id="coverDeductionNotice" name="deductionNotice" rows="3" maxlength="1000">${escape(model.deductionNotice)}</textarea>
            <p class="cover-help">${readOnly ? "Die automatische Berechnung berücksichtigt Rechtschreibfehler." : "Der Hinweis ist frei bearbeitbar. Die automatische Berechnung berücksichtigt Rechtschreibfehler."}</p>
            ${readOnly ? "" : '<button type="button" class="btn btn-secondary btn-sm" id="resetCoverDeductionNotice">Standardtext verwenden</button>'}</div>
          <div class="form-group"><label class="form-label" for="coverInstructions">Zusätzliche Hinweise (optional)</label>
            <textarea class="form-control" id="coverInstructions" name="instructions" rows="3" maxlength="2000">${escape(s.instructions)}</textarea></div>
          <h3>Aufgabenstellungen</h3><p class="cover-help">Aufgaben und Höchstpunkte stammen aus der Klausur.</p>
          ${model.tasks.map((t, i) => `<div class="form-group"><label class="form-label" for="coverTask${i}">${escape(t.title)} · ${escape(t.points)}</label>
            <textarea class="form-control" id="coverTask${i}" data-cover-task="${t.taskNumber}" rows="4" maxlength="4000">${escape(t.text)}</textarea></div>`).join("")}
          <fieldset class="cover-options"><legend>Operatoren auf der ersten Seite</legend>
            <p class="cover-help">Bis zu sechs auswählen, damit die Erklärungen lesbar bleiben. Die Definitionen stammen aus der bereitgestellten Operatorenliste (Stand: 1. August 2025). Die AFB-Zuordnung hängt vom Aufgabenkontext ab.</p>
            ${readOnly ? "" : '<button type="button" class="btn btn-secondary" id="detectCoverOperators">Operatoren aus Aufgaben übernehmen</button><p class="cover-help">Ersetzt die aktuelle Auswahl durch erkannte Operatoren. Die Auswahl kann anschließend angepasst werden. Bitte die Erkennung prüfen.</p><p class="cover-help" data-detection-status role="status"></p>'}
            <label class="form-label" for="coverOperatorSearch">Operator suchen</label>
            <input class="form-control" id="coverOperatorSearch" type="search" placeholder="z. B. erklären">
            <p class="cover-help" data-operator-count role="status"></p><div class="cover-operator-list">
              ${operators.map(o => `<label class="cover-operator" data-operator-row>
                <input type="checkbox" name="operator" value="${escape(o.id)}" ${selected.has(o.id) ? "checked" : ""}>
                <span><strong>${escape(o.name)}</strong> <small>AFB ${escape(o.requirementArea)}</small>
                  <span class="cover-operator-definition">${escape(o.definition)}</span></span></label>`).join("")}
            </div>
          </fieldset>
          <p data-error class="cover-error" role="alert"></p>
        </form><section class="cover-preview-panel" aria-label="Vorschau" tabindex="0">
          <p class="cover-help">Inhaltsvorschau · Seitenumbrüche im heruntergeladenen Dokument prüfen.</p><div data-preview></div>
        </section></div>`;
      const current = active = { dialog, model, examId, readOnly, dirty: !readOnly && !model.isSaved, revision: 0, saving: null, timer: null,
        operatorOrder: s.operators.map(o => o.id) };
      document.body.appendChild(dialog);
      dialog.showModal();
      const toolbar = dialog.querySelector(".cover-toolbar");
      const updateToolbarHeight = () => dialog.style.setProperty("--cover-toolbar-height", `${toolbar.getBoundingClientRect().height}px`);
      updateToolbarHeight();
      current.toolbarObserver = new ResizeObserver(updateToolbarHeight);
      current.toolbarObserver.observe(toolbar);
      renderPreview(current);
      const updateOperatorLimit = () => {
        const inputs = [...dialog.querySelectorAll('input[name="operator"]')];
        const count = inputs.filter(i => i.checked).length;
        inputs.forEach(i => { i.disabled = readOnly || (count >= 6 && !i.checked); });
        dialog.querySelector("[data-operator-count]").textContent = `${count} von 6 Operatoren ausgewählt`;
      };
      updateOperatorLimit();
      dialog.querySelector("#detectCoverOperators")?.addEventListener("click", async () => {
        try {
          await flush();
          if (current !== active) return;
          const ids = [...new Set(current.model.tasks.flatMap(task => task.recognizedOperatorIds))];
          const message = dialog.querySelector("[data-detection-status]");
          if (!ids.length) { message.textContent = "Keine Operatoren erkannt. Du kannst sie unten manuell auswählen."; return; }
          const selectedIds = ids.slice(0, 6);
          dialog.querySelectorAll('input[name="operator"]').forEach(input => { input.checked = selectedIds.includes(input.value); });
          current.operatorOrder = selectedIds;
          message.textContent = ids.length > 6 ? `Es wurden ${ids.length} Operatoren erkannt; die ersten sechs wurden ausgewählt. Bitte die Auswahl prüfen.` : `${ids.length} Operatoren erkannt und ausgewählt. Bitte die Auswahl prüfen.`;
          dialog.querySelector("form").dispatchEvent(new Event("input", { bubbles: true }));
        } catch (err) { notify(err.message, "error"); }
      });
      if (readOnly) dialog.querySelectorAll('form input:not(#coverOperatorSearch), form textarea').forEach(input => {
        if (input.type === "checkbox") input.disabled = true;
        else input.readOnly = true;
      });
      dialog.querySelector("form").addEventListener("submit", e => e.preventDefault());
      dialog.querySelector("form").addEventListener("input", e => {
        if (e.target.id === "coverOperatorSearch") return;
        if (current.readOnly) return;
        const form = dialog.querySelector("form");
        if (e.target.name === "deductionNotice" && form.elements.deductionNotice.value.trim())
          form.elements.showDeductionRules.checked = true;
        if (e.target.name === "showDeductionRules" && form.elements.showDeductionRules.checked && !form.elements.deductionNotice.value.trim())
          form.elements.deductionNotice.value = current.model.defaultDeductionNotice;
        if (e.target.name === "deductionNotice" || e.target.name === "showDeductionRules") {
          const notice = dialog.querySelector("[data-notice-preview]");
          notice.textContent = form.elements.deductionNotice.value.trim();
          notice.hidden = !form.elements.showDeductionRules.checked || !notice.textContent;
        }
        if (e.target.name === "operator") {
          current.operatorOrder = current.operatorOrder.filter(id => id !== e.target.value);
          if (e.target.checked) current.operatorOrder.push(e.target.value);
        }
        current.dirty = true; current.revision++;
        status(current, dialog.querySelector("form").checkValidity() ? "Nicht gespeichert" : "Eingabe prüfen");
        updateOperatorLimit();
        clearTimeout(current.timer);
        current.timer = setTimeout(() => flush({ quiet: true }).catch(() => {}), 650);
      });
      dialog.querySelector("#coverOperatorSearch").addEventListener("input", e => {
        const query = e.target.value.toLocaleLowerCase("de");
        dialog.querySelectorAll("[data-operator-row]").forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase("de").includes(query); });
      });
      dialog.querySelector("#resetCoverDeductionNotice")?.addEventListener("click", () => {
        const notice = dialog.querySelector("#coverDeductionNotice");
        notice.value = current.model.defaultDeductionNotice;
        dialog.querySelector('input[name="showDeductionRules"]').checked = true;
        notice.dispatchEvent(new Event("input", { bubbles: true }));
        notice.focus();
      });
      const close = async () => {
        try { await flush(); if (current === active) clear(); }
        catch (err) { notify(err.message, "error"); }
      };
      dialog.addEventListener("cancel", e => { e.preventDefault(); close(); });
      dialog.querySelector("[data-close]").addEventListener("click", close);
      dialog.querySelectorAll("[data-download]").forEach(b => b.addEventListener("click", () => download(current, b.dataset.download)));
      // Freeze the initial selection, too, even when the user exports without editing.
      await flush();
    } catch (err) { notify(err.message, "error"); }
    finally { opening = false; }
  }

  return { open, flush, clear };
}
