#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATE_PATH = path.join(HERE, '..', 'templates', 'dossier.html');
const SCHEMA_VERSION = 1;
const CRITICALITIES = new Set(['blocker', 'material', 'watch']);
const ROUND_KINDS = new Set(['round', 'final']);
const ROUND_STATUSES = new Set(['framing', 'awaiting-decisions', 'researching', 'ready']);
const THREAD_STATUSES = new Set(['open', 'blocked', 'resolved', 'deferred', 'discarded']);
const LEDGER_STATUSES = new Set(['open', 'blocked', 'decided', 'resolved', 'deferred', 'discarded']);
const BLOCK_TYPES = new Set(['prose', 'list', 'steps', 'table', 'cards', 'code', 'flow', 'callout', 'image']);

const UI = {
  en: {
    skip: 'Skip to content', index: 'Dossier index', previous: 'Previous round', current: 'Current round',
    round: 'Planning round', final: 'Final plan', branch: 'Branch', scope: 'Scope', source: 'Source', date: 'Date',
    evidence: 'Evidence', evidenceTitle: 'What the repository establishes', evidenceIntro: 'Observed facts that constrain this planning round.',
    frontier: 'Decision frontier', frontierTitle: 'Choices ready for review', frontierIntro: 'Recommendations are highlighted, never preselected. Every active item needs an explicit disposition.',
    threads: 'Loose threads', threadsTitle: 'Accounted-for uncertainty', threadsIntro: 'Open threads need a disposition; blocked threads remain visible until their dependency clears.',
    recommendation: 'Recommended', reversibility: 'Reversibility', complexity: 'Complexity', uncertainty: 'Uncertainty', surfaces: 'Surfaces',
    disposition: 'Disposition', chooseDisposition: 'Choose a disposition…', decide: 'Decide now', investigate: 'Investigate next', defer: 'Defer explicitly', discard: 'Discard', resolve: 'Resolve',
    notes: 'Rationale, constraint, or correction', blockedBy: 'Blocked by', reactivation: 'Reactivation',
    corrections: 'Cross-cutting corrections', correctionsHelp: 'Add context that affects more than one card.',
    nextIntent: 'Next intent', continuePlanning: 'Continue planning', continueHelp: 'Apply these decisions and expose the next frontier.', investigateIntent: 'Investigate marked threads', investigateHelp: 'Research before presenting more choices.', consolidate: 'Consolidate final plan', consolidateHelp: 'Allowed only when no material uncertainty remains.',
    reset: 'Reset', copyMarkdown: 'Copy Markdown', copyHandoff: 'Copy handoff', copied: 'Copied', progress: 'Frontier complete',
    saved: 'Saved locally.', restored: 'Local draft restored.', saveFailed: 'Local autosave is unavailable.', resetStatus: 'Local draft cleared.',
    incomplete: 'Complete', ready: 'Ready to copy.', intentMissing: 'next intent', fixFields: 'Complete the highlighted fields.', copiedStatus: 'Markdown copied.', copyFailed: 'Clipboard access failed.',
    continueTitle: 'Continue planning dossier', decisionsHeading: 'Decisions', threadsHeading: 'Thread dispositions', correctionsHeading: 'Corrections', none: 'None',
    handoffTitle: 'Implementation handoff', startWith: 'Start with', objective: 'Objective', planningState: 'Planning state', rounds: 'Rounds', decisions: 'Decisions', openThreads: 'Open threads',
    timeline: 'Timeline', timelineTitle: 'Planning record', timelineIntro: 'Immutable rounds show how the plan changed; the index shows current state.', ledger: 'Current ledger', ledgerTitle: 'Decisions and threads',
    id: 'ID', item: 'Item', status: 'Status', outcome: 'Outcome', noLedger: 'No ledger items yet.', currentSummary: 'Current state', finalHandoff: 'Handoff', finalHandoffIntro: 'The next agent reads the dossier instead of receiving the plan again.',
    generated: 'Generated planning artifact', immutable: 'Round is immutable · index carries current state'
  },
  es: {
    skip: 'Saltar al contenido', index: 'Índice del dossier', previous: 'Ronda anterior', current: 'Ronda actual',
    round: 'Ronda de planificación', final: 'Plan final', branch: 'Rama', scope: 'Alcance', source: 'Fuente', date: 'Fecha',
    evidence: 'Evidencia', evidenceTitle: 'Lo que establece el repositorio', evidenceIntro: 'Hechos observados que restringen esta ronda de planificación.',
    frontier: 'Frontera de decisiones', frontierTitle: 'Decisiones listas para revisión', frontierIntro: 'Las recomendaciones se destacan, nunca se preseleccionan. Cada elemento activo necesita una disposición explícita.',
    threads: 'Hilos sueltos', threadsTitle: 'Incertidumbre contabilizada', threadsIntro: 'Los hilos abiertos requieren disposición; los bloqueados siguen visibles hasta resolver su dependencia.',
    recommendation: 'Recomendada', reversibility: 'Reversibilidad', complexity: 'Complejidad', uncertainty: 'Incertidumbre', surfaces: 'Superficies',
    disposition: 'Disposición', chooseDisposition: 'Elige una disposición…', decide: 'Decidir ahora', investigate: 'Investigar después', defer: 'Aplazar explícitamente', discard: 'Descartar', resolve: 'Resolver',
    notes: 'Justificación, restricción o corrección', blockedBy: 'Bloqueado por', reactivation: 'Reactivación',
    corrections: 'Correcciones transversales', correctionsHelp: 'Añade contexto que afecte a más de una tarjeta.',
    nextIntent: 'Siguiente intención', continuePlanning: 'Continuar planificación', continueHelp: 'Aplicar decisiones y exponer la siguiente frontera.', investigateIntent: 'Investigar hilos marcados', investigateHelp: 'Investigar antes de presentar más opciones.', consolidate: 'Consolidar plan final', consolidateHelp: 'Solo si no queda incertidumbre material.',
    reset: 'Restablecer', copyMarkdown: 'Copiar Markdown', copyHandoff: 'Copiar handoff', copied: 'Copiado', progress: 'Frontera completa',
    saved: 'Guardado localmente.', restored: 'Borrador local restaurado.', saveFailed: 'El autoguardado local no está disponible.', resetStatus: 'Borrador local eliminado.',
    incomplete: 'Completa', ready: 'Listo para copiar.', intentMissing: 'siguiente intención', fixFields: 'Completa los campos resaltados.', copiedStatus: 'Markdown copiado.', copyFailed: 'No se pudo acceder al portapapeles.',
    continueTitle: 'Continuar planning dossier', decisionsHeading: 'Decisiones', threadsHeading: 'Disposición de hilos', correctionsHeading: 'Correcciones', none: 'Ninguno',
    handoffTitle: 'Handoff de implementación', startWith: 'Empezar por', objective: 'Objetivo', planningState: 'Estado del plan', rounds: 'Rondas', decisions: 'Decisiones', openThreads: 'Hilos abiertos',
    timeline: 'Timeline', timelineTitle: 'Registro de planificación', timelineIntro: 'Las rondas inmutables muestran cómo cambió el plan; el índice muestra el estado actual.', ledger: 'Registro actual', ledgerTitle: 'Decisiones e hilos',
    id: 'ID', item: 'Elemento', status: 'Estado', outcome: 'Resultado', noLedger: 'Todavía no hay elementos en el registro.', currentSummary: 'Estado actual', finalHandoff: 'Handoff', finalHandoffIntro: 'El siguiente agente lee el dossier en vez de recibir de nuevo el plan.',
    generated: 'Artefacto de planificación generado', immutable: 'Ronda inmutable · el índice conserva el estado actual'
  }
};

function fail(message) { throw new Error(message); }
function isObject(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function needObject(value, label) { if (!isObject(value)) fail(`${label} must be an object`); }
function needArray(value, label) { if (!Array.isArray(value)) fail(`${label} must be an array`); }
function needString(value, label) { if (typeof value !== 'string' || !value.trim()) fail(`${label} must be a non-empty string`); }
function optionalString(value, label) { if (value !== undefined && (typeof value !== 'string' || !value.trim())) fail(`${label} must be omitted or a non-empty string`); }
function esc(value) { return String(value ?? '').replace(/[&<>'"]/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' }[c])); }
function slug(value, label) { needString(value, label); if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)) fail(`${label} must be a lowercase kebab-case slug`); }
function pad(number) { return String(number).padStart(2, '0'); }
function normalizedRelative(value) {
  needString(value, 'outputDir');
  const normalized = value.replace(/\\/g, '/').replace(/^\.\//, '').replace(/\/$/, '');
  if (path.isAbsolute(value) || normalized.startsWith('../') || normalized.includes('/../')) fail('outputDir must be repository-relative and cannot traverse upward');
  return normalized;
}
function safeJson(value) { return JSON.stringify(value).replace(/</g, '\\u003c').replace(/>/g, '\\u003e').replace(/&/g, '\\u0026'); }
function countOpen(items) { return items.filter(item => item.status === 'open' || item.status === 'blocked').length; }
function criticalityCounts(round) {
  const counts = { blocker:0, material:0, watch:0 };
  round.decisions.forEach(item => { counts[item.criticality] += 1; });
  round.threads.filter(item => item.status === 'open' || item.status === 'blocked').forEach(item => { counts[item.criticality] += 1; });
  return counts;
}

function validateOption(option, label) {
  needObject(option, label);
  ['id','label','summary','reversibility','complexity','uncertainty'].forEach(key => needString(option[key], `${label}.${key}`));
  slug(option.id, `${label}.id`);
  needArray(option.tradeoffs, `${label}.tradeoffs`);
  needArray(option.surfaces, `${label}.surfaces`);
  option.tradeoffs.forEach((item, i) => needString(item, `${label}.tradeoffs[${i}]`));
  option.surfaces.forEach((item, i) => needString(item, `${label}.surfaces[${i}]`));
  if (!['S','M','L'].includes(option.complexity)) fail(`${label}.complexity must be S, M, or L`);
  if (!['low','medium','high'].includes(option.uncertainty)) fail(`${label}.uncertainty must be low, medium, or high`);
}

function validateDecision(decision, index) {
  const label = `round.decisions[${index}]`;
  needObject(decision, label);
  ['id','title','question','rationale','recommendation'].forEach(key => needString(decision[key], `${label}.${key}`));
  optionalString(decision.notesPrompt, `${label}.notesPrompt`);
  if (!CRITICALITIES.has(decision.criticality)) fail(`${label}.criticality is invalid`);
  if (!['single','multiple'].includes(decision.mode)) fail(`${label}.mode must be single or multiple`);
  needArray(decision.options, `${label}.options`);
  if (decision.options.length < 2 || decision.options.length > 4) fail(`${label}.options must contain 2–4 options`);
  decision.options.forEach((option, optionIndex) => validateOption(option, `${label}.options[${optionIndex}]`));
  const ids = decision.options.map(option => option.id);
  if (new Set(ids).size !== ids.length) fail(`${label}.options contains duplicate IDs`);
  if (!ids.includes(decision.recommendation)) fail(`${label}.recommendation must name an option ID`);
}

function validateThread(thread, index) {
  const label = `round.threads[${index}]`;
  needObject(thread, label);
  ['id','title','detail'].forEach(key => needString(thread[key], `${label}.${key}`));
  if (!CRITICALITIES.has(thread.criticality)) fail(`${label}.criticality is invalid`);
  if (!THREAD_STATUSES.has(thread.status)) fail(`${label}.status is invalid`);
  if (thread.blockedBy !== undefined) {
    needArray(thread.blockedBy, `${label}.blockedBy`);
    thread.blockedBy.forEach((item, i) => needString(item, `${label}.blockedBy[${i}]`));
  }
  if (thread.status === 'blocked' && (!thread.blockedBy || !thread.blockedBy.length)) fail(`${label}.blockedBy is required for blocked threads`);
  if (thread.status === 'deferred') {
    ['reason','impact','reactivation'].forEach(key => needString(thread[key], `${label}.${key}`));
  }
  if (thread.status === 'resolved' || thread.status === 'discarded') needString(thread.resolution, `${label}.resolution`);
}

function validateBlock(block, sectionIndex, blockIndex) {
  const label = `round.sections[${sectionIndex}].blocks[${blockIndex}]`;
  needObject(block, label);
  if (!BLOCK_TYPES.has(block.type)) fail(`${label}.type is invalid`);
  if (block.type === 'prose') {
    needArray(block.paragraphs, `${label}.paragraphs`);
    block.paragraphs.forEach((item, i) => needString(item, `${label}.paragraphs[${i}]`));
  } else if (block.type === 'list') {
    needString(block.title, `${label}.title`); needArray(block.items, `${label}.items`); block.items.forEach((item, i) => needString(item, `${label}.items[${i}]`));
  } else if (block.type === 'steps') {
    needArray(block.items, `${label}.items`); block.items.forEach((item, i) => needString(item, `${label}.items[${i}]`));
  } else if (block.type === 'table') {
    needArray(block.columns, `${label}.columns`); needArray(block.rows, `${label}.rows`);
    block.columns.forEach((column, i) => { needObject(column, `${label}.columns[${i}]`); needString(column.key, `${label}.columns[${i}].key`); needString(column.label, `${label}.columns[${i}].label`); });
    block.rows.forEach((row, i) => { needObject(row, `${label}.rows[${i}]`); block.columns.forEach(column => { if (!['string','number','boolean'].includes(typeof row[column.key])) fail(`${label}.rows[${i}].${column.key} must be scalar`); }); });
  } else if (block.type === 'cards') {
    needArray(block.items, `${label}.items`);
    block.items.forEach((item, i) => { needObject(item, `${label}.items[${i}]`); needString(item.title, `${label}.items[${i}].title`); needString(item.body, `${label}.items[${i}].body`); optionalString(item.badge, `${label}.items[${i}].badge`); if (item.meta !== undefined) { needArray(item.meta, `${label}.items[${i}].meta`); item.meta.forEach((meta, j) => needString(meta, `${label}.items[${i}].meta[${j}]`)); } });
  } else if (block.type === 'code') {
    needString(block.label, `${label}.label`); needString(block.code, `${label}.code`);
  } else if (block.type === 'flow') {
    needArray(block.nodes, `${label}.nodes`); if (block.nodes.length < 2) fail(`${label}.nodes needs at least two nodes`);
    block.nodes.forEach((node, i) => { needObject(node, `${label}.nodes[${i}]`); needString(node.title, `${label}.nodes[${i}].title`); needString(node.detail, `${label}.nodes[${i}].detail`); });
  } else if (block.type === 'callout') {
    if (!['decision','warning','success'].includes(block.tone)) fail(`${label}.tone is invalid`); needString(block.title, `${label}.title`); needString(block.body, `${label}.body`);
  } else if (block.type === 'image') {
    needString(block.src, `${label}.src`); needString(block.alt, `${label}.alt`); optionalString(block.caption, `${label}.caption`);
  }
}

function validateInput(input) {
  needObject(input, 'input');
  if (input.schemaVersion !== SCHEMA_VERSION) fail(`schemaVersion must be ${SCHEMA_VERSION}`);
  input.outputDir = normalizedRelative(input.outputDir);
  needObject(input.dossier, 'dossier');
  ['id','project','title','objective','language','scope'].forEach(key => needString(input.dossier[key], `dossier.${key}`));
  slug(input.dossier.id, 'dossier.id');
  optionalString(input.dossier.branch, 'dossier.branch'); optionalString(input.dossier.sourceRef, 'dossier.sourceRef');
  needObject(input.round, 'round');
  if (!Number.isInteger(input.round.number) || input.round.number < 1) fail('round.number must be a positive integer');
  slug(input.round.slug, 'round.slug');
  ['date','title','summary','status'].forEach(key => needString(input.round[key], `round.${key}`));
  if (!ROUND_KINDS.has(input.round.kind)) fail('round.kind is invalid');
  if (!ROUND_STATUSES.has(input.round.status)) fail('round.status is invalid');
  ['evidence','decisions','threads','sections'].forEach(key => needArray(input.round[key], `round.${key}`));
  input.round.evidence.forEach((evidence, index) => { needObject(evidence, `round.evidence[${index}]`); needString(evidence.title, `round.evidence[${index}].title`); needString(evidence.detail, `round.evidence[${index}].detail`); needArray(evidence.refs, `round.evidence[${index}].refs`); evidence.refs.forEach((ref, refIndex) => needString(ref, `round.evidence[${index}].refs[${refIndex}]`)); });
  input.round.decisions.forEach(validateDecision);
  input.round.threads.forEach(validateThread);
  const itemIds = [...input.round.decisions, ...input.round.threads].map(item => item.id);
  if (new Set(itemIds).size !== itemIds.length) fail('round decision/thread IDs must be unique');
  input.round.sections.forEach((section, sectionIndex) => { needObject(section, `round.sections[${sectionIndex}]`); slug(section.id, `round.sections[${sectionIndex}].id`); needString(section.label, `round.sections[${sectionIndex}].label`); needString(section.title, `round.sections[${sectionIndex}].title`); optionalString(section.intro, `round.sections[${sectionIndex}].intro`); needArray(section.blocks, `round.sections[${sectionIndex}].blocks`); section.blocks.forEach((block, blockIndex) => validateBlock(block, sectionIndex, blockIndex)); });
  if (!input.round.evidence.length && !input.round.decisions.length && !input.round.threads.length && !input.round.sections.length) fail('round must contain useful content');
  needObject(input.currentState, 'currentState');
  needString(input.currentState.status, 'currentState.status'); needString(input.currentState.summary, 'currentState.summary');
  needArray(input.currentState.decisions, 'currentState.decisions'); needArray(input.currentState.threads, 'currentState.threads');
  [...input.currentState.decisions, ...input.currentState.threads].forEach((item, index) => { needObject(item, `currentState item ${index}`); needString(item.id, `currentState item ${index}.id`); needString(item.title, `currentState item ${index}.title`); if (!LEDGER_STATUSES.has(item.status)) fail(`currentState item ${index}.status is invalid`); if (item.status !== 'open') needString(item.outcome, `currentState item ${index}.outcome`); else if (item.outcome === undefined) item.outcome = ''; });
  input.currentState.threads.forEach((thread, index) => { if (!CRITICALITIES.has(thread.criticality)) fail(`currentState.threads[${index}].criticality is invalid`); });
  const ledgerIds = [...input.currentState.decisions, ...input.currentState.threads].map(item => item.id);
  if (new Set(ledgerIds).size !== ledgerIds.length) fail('currentState IDs must be unique');
  if (input.round.kind === 'final') {
    if (input.round.decisions.length) fail('final round cannot contain active decisions');
    const unresolved = [...input.currentState.decisions, ...input.currentState.threads].filter(item => item.status === 'open' || item.status === 'blocked');
    if (unresolved.length) fail(`final round has unresolved ledger items: ${unresolved.map(item => item.id).join(', ')}`);
    needObject(input.round.finalHandoff, 'round.finalHandoff');
    ['title','instruction','startWith'].forEach(key => needString(input.round.finalHandoff[key], `round.finalHandoff.${key}`));
  }
  return input;
}

function template() { return fs.readFileSync(TEMPLATE_PATH, 'utf8'); }
function applyTemplate({ lang, title, description, body, data }) {
  return template()
    .replaceAll('@@LANG@@', esc(lang))
    .replaceAll('@@TITLE@@', esc(title))
    .replaceAll('@@DESCRIPTION@@', esc(description))
    .replaceAll('@@SKIP_LABEL@@', esc((UI[lang] || UI.en).skip))
    .replace('@@BODY@@', body)
    .replace('@@DATA_JSON@@', safeJson(data));
}
function badge(criticality, label = criticality) { return `<span class="badge ${esc(criticality)}">${esc(label)}</span>`; }
function nav(output, previous, ui, currentFile) {
  const prev = previous ? `<a href="${esc(previous.file)}">← ${esc(ui.previous)} · ${pad(previous.number)}</a>` : '';
  return `<nav class="topnav" aria-label="${esc(ui.timeline)}"><div class="topnav-group"><a href="index.html">← ${esc(ui.index)}</a>${prev}</div><span>${esc(ui.current)} · ${esc(currentFile)}</span></nav>`;
}
function meta(dossier, round, ui) {
  const values = [[ui.date, round.date], [ui.scope, dossier.scope]];
  if (dossier.branch) values.push([ui.branch, `<code>${esc(dossier.branch)}</code>`]);
  if (dossier.sourceRef) values.push([ui.source, `<code>${esc(dossier.sourceRef)}</code>`]);
  return `<dl class="masthead-meta">${values.map(([key,value]) => `<div><dt>${esc(key)}</dt><dd>${value}</dd></div>`).join('')}</dl>`;
}
function masthead(dossier, round, ui, isIndex = false) {
  const kind = isIndex ? ui.index : (round.kind === 'final' ? ui.final : `${ui.round} · ${pad(round.number)}`);
  const emphasized = esc(round.title).replace(/\s+([^\s]+)$/, ' <em>$1</em>');
  return `<header class="masthead"><p class="kicker">${esc(dossier.project)} · ${esc(kind)}</p><h1>${emphasized}</h1><span class="stamp">${esc(round.status)} · ${isIndex ? esc(ui.current) : pad(round.number)}</span><p class="lede">${esc(round.summary)}</p>${meta(dossier, round, ui)}</header>`;
}
function tally(counts, ui) {
  return `<div class="tally"><div class="cell blocker"><div class="num">${counts.blocker}</div><div class="lbl">Blocker</div></div><div class="cell material"><div class="num">${counts.material}</div><div class="lbl">Material</div></div><div class="cell watch"><div class="num">${counts.watch}</div><div class="lbl">Watch</div></div></div>`;
}
function sectionHead(label, title, intro) {
  return `<div class="section-head"><span class="section-label">${esc(label)}</span><h2>${esc(title)}</h2></div>${intro ? `<p class="section-intro">${esc(intro)}</p>` : ''}`;
}
function renderEvidence(round, ui) {
  if (!round.evidence.length) return '';
  return `<section class="section" id="evidence">${sectionHead(ui.evidence, ui.evidenceTitle, ui.evidenceIntro)}<div class="evidence-grid">${round.evidence.map(item => `<article class="evidence-card"><h3>${esc(item.title)}</h3><p>${esc(item.detail)}</p>${item.refs.length ? `<ul class="refs">${item.refs.map(ref => `<li><code>${esc(ref)}</code></li>`).join('')}</ul>` : ''}</article>`).join('')}</div></section>`;
}
function optionCard(decision, option, ui) {
  const inputType = decision.mode === 'single' ? 'radio' : 'checkbox';
  const recommended = decision.recommendation === option.id ? `<span class="recommended">${esc(ui.recommendation)}</span>` : '';
  return `<label class="option"><span class="option-top"><input data-state type="${inputType}" name="decision.${esc(decision.id)}.option" value="${esc(option.id)}"><span class="option-title">${esc(option.label)}</span>${recommended}</span><span class="option-summary">${esc(option.summary)}</span><dl class="option-meta"><div><dt>${esc(ui.reversibility)}</dt><dd>${esc(option.reversibility)}</dd></div><div><dt>${esc(ui.complexity)}</dt><dd>${esc(option.complexity)}</dd></div><div><dt>${esc(ui.uncertainty)}</dt><dd>${esc(option.uncertainty)}</dd></div></dl>${option.tradeoffs.length ? `<ul class="tradeoffs">${option.tradeoffs.map(item => `<li>${esc(item)}</li>`).join('')}</ul>` : ''}${option.surfaces.length ? `<ul class="meta-list" aria-label="${esc(ui.surfaces)}">${option.surfaces.map(item => `<li>${esc(item)}</li>`).join('')}</ul>` : ''}</label>`;
}
function dispositionSelect(name, id, ui, thread = false) {
  return `<select data-state id="${esc(id)}" name="${esc(name)}"><option value="">${esc(ui.chooseDisposition)}</option><option value="${thread ? 'resolve' : 'decide'}">${esc(thread ? ui.resolve : ui.decide)}</option><option value="investigate">${esc(ui.investigate)}</option><option value="defer">${esc(ui.defer)}</option><option value="discard">${esc(ui.discard)}</option></select>`;
}
function renderDecision(decision, ui) {
  return `<article class="decision-card" data-criticality="${esc(decision.criticality)}"><div class="card-head"><span class="folio" aria-hidden="true">${esc(decision.id.replace(/\D/g,''))}</span><h3>${esc(decision.title)}</h3><div class="card-head-right">${badge(decision.criticality)}</div></div><div class="card-copy"><p class="question">${esc(decision.question)}</p><p class="rationale">${esc(decision.rationale)}</p></div><div class="option-grid">${decision.options.map(option => optionCard(decision, option, ui)).join('')}</div><div class="response"><div class="field"><label for="disp-${esc(decision.id)}">${esc(ui.disposition)}</label>${dispositionSelect(`decision.${decision.id}.disposition`, `disp-${decision.id}`, ui)}</div><div class="field"><label for="note-${esc(decision.id)}">${esc(decision.notesPrompt || ui.notes)}</label><textarea data-state id="note-${esc(decision.id)}" name="decision.${esc(decision.id)}.note"></textarea></div></div></article>`;
}
function renderDecisions(round, ui) {
  if (!round.decisions.length) return '';
  return `<section class="section" id="decision-frontier">${sectionHead(ui.frontier, ui.frontierTitle, ui.frontierIntro)}<div class="decision-list">${round.decisions.map(item => renderDecision(item, ui)).join('')}</div></section>`;
}
function renderThread(thread, ui) {
  const blocked = thread.status === 'blocked';
  const stateBadge = thread.status === 'resolved' || thread.status === 'discarded' ? badge('done', thread.status) : badge(thread.criticality);
  const extra = thread.reactivation ? `<p class="blocked-by">${esc(ui.reactivation)} · ${esc(thread.reactivation)}</p>` : '';
  const resolution = thread.resolution ? `<p class="blocked-by">${esc(thread.resolution)}</p>` : '';
  const controls = thread.status === 'open' ? `<div class="response interactive-only"><div class="field"><label for="thread-disp-${esc(thread.id)}">${esc(ui.disposition)}</label>${dispositionSelect(`thread.${thread.id}.disposition`, `thread-disp-${thread.id}`, ui, true)}</div><div class="field"><label for="thread-note-${esc(thread.id)}">${esc(ui.notes)}</label><textarea data-state id="thread-note-${esc(thread.id)}" name="thread.${esc(thread.id)}.note"></textarea></div></div>` : '';
  return `<article class="thread-card ${blocked ? 'blocked' : ''}"><div class="card-head"><span class="folio" aria-hidden="true">${esc(thread.id.replace(/\D/g,''))}</span><h3>${esc(thread.title)}</h3><div class="card-head-right">${stateBadge}</div></div><div class="thread-body"><p>${esc(thread.detail)}</p>${blocked ? `<p class="blocked-by">${esc(ui.blockedBy)} · ${esc((thread.blockedBy || []).join(', '))}</p>` : ''}${extra}${resolution}</div>${controls}</article>`;
}
function renderThreads(round, ui) {
  if (!round.threads.length) return '';
  return `<section class="section" id="loose-threads">${sectionHead(ui.threads, ui.threadsTitle, ui.threadsIntro)}<div class="thread-list">${round.threads.map(item => renderThread(item, ui)).join('')}</div></section>`;
}
function renderBlock(block) {
  if (block.type === 'prose') return `<div class="prose">${block.paragraphs.map(item => `<p>${esc(item)}</p>`).join('')}</div>`;
  if (block.type === 'list') return `<div class="list-block"><h3>${esc(block.title)}</h3><ul class="outcomes">${block.items.map(item => `<li>${esc(item)}</li>`).join('')}</ul></div>`;
  if (block.type === 'steps') return `<div class="steps-block"><ol class="steps">${block.items.map(item => `<li>${esc(item)}</li>`).join('')}</ol></div>`;
  if (block.type === 'table') return `<div class="table-wrap"><table class="order-table"><thead><tr>${block.columns.map(column => `<th>${esc(column.label)}</th>`).join('')}</tr></thead><tbody>${block.rows.map(row => `<tr>${block.columns.map(column => `<td>${esc(row[column.key])}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
  if (block.type === 'cards') return `<div class="cards-grid">${block.items.map(item => `<article class="mini-card">${item.badge ? `<span class="badge">${esc(item.badge)}</span>` : ''}<h3>${esc(item.title)}</h3><p>${esc(item.body)}</p>${item.meta?.length ? `<ul class="meta-list">${item.meta.map(meta => `<li>${esc(meta)}</li>`).join('')}</ul>` : ''}</article>`).join('')}</div>`;
  if (block.type === 'code') return `<div class="code-block"><div class="code-label">${esc(block.label)}</div><pre><code>${esc(block.code)}</code></pre></div>`;
  if (block.type === 'flow') return `<div class="flow-block"><div class="flow">${block.nodes.map((node, index) => `${index ? '<span class="flow-arrow" aria-hidden="true">→</span>' : ''}<div class="flow-node"><strong>${esc(node.title)}</strong><span>${esc(node.detail)}</span></div>`).join('')}</div></div>`;
  if (block.type === 'callout') return `<div class="callout ${esc(block.tone)}"><b>${esc(block.title)}</b><p>${esc(block.body)}</p></div>`;
  if (block.type === 'image') return `<figure class="visual-block"><img src="${esc(block.src)}" alt="${esc(block.alt)}" loading="lazy">${block.caption ? `<figcaption>${esc(block.caption)}</figcaption>` : ''}</figure>`;
  return '';
}
function renderSections(round) {
  return round.sections.map(section => `<section class="section" id="${esc(section.id)}">${sectionHead(section.label, section.title, section.intro)}<div class="content-stack">${section.blocks.map(renderBlock).join('')}</div></section>`).join('');
}
function intentControls(ui) {
  return `<section class="section interactive-only" id="next-intent">${sectionHead(ui.nextIntent, ui.nextIntent, '')}<div class="intent-grid"><label class="intent"><input data-state type="radio" name="intent" value="continue"><span><b>${esc(ui.continuePlanning)}</b><small>${esc(ui.continueHelp)}</small></span></label><label class="intent"><input data-state type="radio" name="intent" value="investigate"><span><b>${esc(ui.investigateIntent)}</b><small>${esc(ui.investigateHelp)}</small></span></label><label class="intent"><input data-state type="radio" name="intent" value="consolidate"><span><b>${esc(ui.consolidate)}</b><small>${esc(ui.consolidateHelp)}</small></span></label></div><div class="field" style="margin-top:18px"><label for="global-notes">${esc(ui.corrections)}</label><textarea data-state id="global-notes" name="globalNotes" placeholder="${esc(ui.correctionsHelp)}"></textarea></div></section>`;
}
function dispatch(ui, final = false) {
  return `<aside class="dispatch interactive-only" aria-label="${esc(final ? ui.finalHandoff : ui.nextIntent)}"><div class="dispatch-grid"><div class="progress"><div class="progress-label">${esc(ui.progress)} · <span id="progress-text">0/0</span></div><div class="progress-track"><div class="progress-fill" id="progress-fill"></div></div><p class="validation-hint" id="validation-hint"></p></div><button class="btn ghost" id="reset-state" type="button">${esc(ui.reset)}</button><button class="btn primary" id="copy-markdown" type="button" disabled>${esc(final ? ui.copyHandoff : ui.copyMarkdown)}</button></div></aside><p class="sr-only" id="live-status" role="status" aria-live="polite"></p>`;
}
function renderRound(data, previous) {
  const { dossier, round, ui } = data;
  const counts = criticalityCounts(round);
  const finalCallout = round.kind === 'final' ? `<section class="section" id="implementation-handoff">${sectionHead(ui.finalHandoff, round.finalHandoff.title, ui.finalHandoffIntro)}<div class="callout success"><b>${esc(ui.startWith)}</b><p>${esc(round.finalHandoff.startWith)}</p></div></section>` : '';
  const formOpen = `<form id="planning-form" novalidate>`;
  const interactive = round.kind === 'final' ? `${renderThreads(round, ui)}${finalCallout}` : `${renderDecisions(round, ui)}${renderThreads(round, ui)}${intentControls(ui)}`;
  return `<div class="sheet">${nav(data.outputDir, previous, ui, round.file)}<main id="main">${masthead(dossier, round, ui)}<section class="summary"><p class="summary-copy"><strong>${esc(ui.objective)}.</strong> ${esc(dossier.objective)}</p>${tally(counts, ui)}</section>${formOpen}${renderEvidence(round, ui)}${renderSections(round)}${interactive}${dispatch(ui, round.kind === 'final')}</form></main><footer class="colophon"><span>${esc(dossier.project)} · planning-dossier</span><span>${esc(ui.immutable)}</span></footer></div>`;
}
function ledgerRows(current, ui) {
  const rows = [...current.decisions, ...current.threads];
  if (!rows.length) return `<p class="section-intro">${esc(ui.noLedger)}</p>`;
  return `<div class="ledger"><div class="ledger-row"><b>${esc(ui.id)}</b><b>${esc(ui.item)}</b><b>${esc(ui.status)}</b><b>${esc(ui.outcome)}</b></div>${rows.map(item => `<div class="ledger-row"><span class="ledger-id">${esc(item.id)}</span><strong>${esc(item.title)}</strong><span>${esc(item.status)}</span><span>${esc(item.outcome || '—')}</span></div>`).join('')}</div>`;
}
function renderIndex(data) {
  const { dossier, manifest, ui } = data;
  const currentRound = manifest.rounds.at(-1);
  const pseudoRound = { ...currentRound, title:dossier.title, summary:manifest.current.summary, date:currentRound.date, status:manifest.current.status, kind:'index', number:currentRound.number };
  const openThreads = countOpen(manifest.current.threads);
  const openDecisions = countOpen(manifest.current.decisions);
  const statusCounts = { blocker:0, material:openDecisions, watch:openThreads };
  const timeline = `<section class="section" id="timeline">${sectionHead(ui.timeline, ui.timelineTitle, ui.timelineIntro)}<div class="timeline">${manifest.rounds.map((round, index) => `<article class="timeline-entry ${index === manifest.rounds.length - 1 ? 'current' : ''}"><span class="timeline-dot">${pad(round.number)}</span><div class="timeline-body"><h3><a href="${esc(round.file)}">${esc(round.title)}</a></h3><p>${esc(round.summary)}</p><div class="timeline-meta"><span>${esc(round.date)}</span><span>${esc(round.kind)}</span><span>${round.decisionCount} ${esc(ui.decisions)}</span><span>${round.threadCount} ${esc(ui.threads)}</span></div></div></article>`).join('')}</div></section>`;
  const ledger = `<section class="section" id="ledger">${sectionHead(ui.ledger, ui.ledgerTitle, '')}${ledgerRows(manifest.current, ui)}</section>`;
  return `<div class="sheet"><nav class="topnav" aria-label="${esc(ui.timeline)}"><div class="topnav-group"><a href="${esc(currentRound.file)}">${esc(ui.current)} →</a></div><span>${esc(data.outputDir)}/index.html</span></nav><main id="main">${masthead(dossier, pseudoRound, ui, true)}<section class="summary"><p class="summary-copy"><strong>${esc(ui.objective)}.</strong> ${esc(dossier.objective)}</p>${tally(statusCounts, ui)}</section>${timeline}${ledger}</main><footer class="colophon"><span>${esc(dossier.project)} · planning-dossier</span><span>${esc(ui.generated)}</span></footer></div>`;
}

function extractData(html, file) {
  const match = html.match(/<script id="planning-data" type="application\/json">([\s\S]*?)<\/script>/);
  if (!match) fail(`${file} has no planning-data payload`);
  try { return JSON.parse(match[1]); } catch (error) { fail(`${file} has invalid planning-data: ${error.message}`); }
}
function readManifest(indexPath) {
  if (!fs.existsSync(indexPath)) return null;
  const data = extractData(fs.readFileSync(indexPath, 'utf8'), indexPath);
  if (!data.manifest) fail(`${indexPath} has no manifest`);
  return data.manifest;
}
function preserveLedger(previous, next, label) {
  if (!previous) return;
  const nextIds = new Set(next.map(item => item.id));
  const missing = previous.filter(item => !nextIds.has(item.id)).map(item => item.id);
  if (missing.length) fail(`${label} silently removes prior IDs: ${missing.join(', ')}`);
}

function renderInput(inputPath, consume) {
  const absoluteInput = path.resolve(inputPath);
  const input = validateInput(JSON.parse(fs.readFileSync(absoluteInput, 'utf8')));
  const outputDir = path.resolve(input.outputDir);
  const indexPath = path.join(outputDir, 'index.html');
  const previousManifest = readManifest(indexPath);
  let rounds = [];
  let previous = null;
  if (previousManifest) {
    if (previousManifest.schemaVersion !== SCHEMA_VERSION) fail('existing dossier schema version is unsupported');
    if (previousManifest.dossier.id !== input.dossier.id) fail('dossier.id does not match existing index');
    if (previousManifest.outputDir !== input.outputDir) fail('outputDir does not match existing index');
    rounds = previousManifest.rounds.slice();
    previous = rounds.at(-1);
    if (input.round.number !== previous.number + 1) fail(`round.number must be ${previous.number + 1}`);
    preserveLedger(previousManifest.current.decisions, input.currentState.decisions, 'currentState.decisions');
    preserveLedger(previousManifest.current.threads, input.currentState.threads, 'currentState.threads');
  } else if (input.round.number !== 1) {
    fail('a new dossier must start at round 1');
  }

  const roundFile = `round-${pad(input.round.number)}-${input.round.slug}.html`;
  const roundPath = path.join(outputDir, roundFile);
  if (fs.existsSync(roundPath)) fail(`${roundFile} already exists; rounds are immutable`);
  fs.mkdirSync(outputDir, { recursive:true });

  const ui = UI[input.dossier.language] || UI.en;
  const round = { ...input.round, file:roundFile };
  const roundRecord = {
    number:round.number, file:roundFile, title:round.title, date:round.date, kind:round.kind,
    status:round.status, summary:round.summary, decisionCount:round.decisions.length, threadCount:round.threads.length
  };
  rounds.push(roundRecord);
  const manifest = { schemaVersion:SCHEMA_VERSION, outputDir:input.outputDir, dossier:input.dossier, rounds, current:input.currentState };
  const roundData = { schemaVersion:SCHEMA_VERSION, outputDir:input.outputDir, dossier:input.dossier, round, currentState:input.currentState, ui };
  const roundHtml = applyTemplate({ lang:input.dossier.language, title:`${round.title} · ${input.dossier.project}`, description:round.summary, body:renderRound(roundData, previous), data:roundData });
  fs.writeFileSync(roundPath, roundHtml, 'utf8');

  const indexRound = { ...roundRecord, decisions:[], threads:[], kind:'index' };
  const indexData = { schemaVersion:SCHEMA_VERSION, outputDir:input.outputDir, dossier:input.dossier, round:indexRound, manifest, ui };
  const indexHtml = applyTemplate({ lang:input.dossier.language, title:`${input.dossier.title} · planning dossier`, description:input.currentState.summary, body:renderIndex(indexData), data:indexData });
  fs.writeFileSync(indexPath, indexHtml, 'utf8');

  if (consume) fs.rmSync(absoluteInput);
  process.stdout.write(`${JSON.stringify({ index:path.relative(process.cwd(), indexPath), round:path.relative(process.cwd(), roundPath), number:round.number, kind:round.kind })}\n`);
}

function verifyDossier(directory) {
  const outputDir = path.resolve(directory);
  const indexPath = path.join(outputDir, 'index.html');
  if (!fs.existsSync(indexPath)) fail('index.html does not exist');
  const indexHtml = fs.readFileSync(indexPath, 'utf8');
  const indexData = extractData(indexHtml, indexPath);
  const manifest = indexData.manifest;
  needObject(manifest, 'index manifest');
  needArray(manifest.rounds, 'index manifest.rounds');
  if (!manifest.rounds.length) fail('index manifest has no rounds');
  const errors = [];
  const files = [{ name:'index.html', html:indexHtml, data:indexData }];
  manifest.rounds.forEach((round, index) => {
    if (round.number !== index + 1) errors.push(`round sequence breaks at ${round.file}`);
    const roundPath = path.join(outputDir, round.file);
    if (!fs.existsSync(roundPath)) { errors.push(`missing ${round.file}`); return; }
    const html = fs.readFileSync(roundPath, 'utf8');
    let data;
    try { data = extractData(html, roundPath); } catch (error) { errors.push(error.message); return; }
    if (data.round.number !== round.number || data.round.file !== round.file) errors.push(`payload mismatch in ${round.file}`);
    files.push({ name:round.file, html, data });
  });
  files.forEach(file => {
    if (/@@(?:LANG|TITLE|DESCRIPTION|SKIP_LABEL|BODY|DATA_JSON)@@/.test(file.html)) errors.push(`unreplaced template slot in ${file.name}`);
    if (/<(?:script|img|iframe)\b[^>]*(?:src)=['"]https?:/i.test(file.html)) errors.push(`remote executable/media source in ${file.name}`);
    const remoteLinks = [...file.html.matchAll(/<link\b[^>]*href=['"](https?:[^'"]+)['"]/gi)].map(match => match[1]);
    remoteLinks.filter(url => !/^https:\/\/fonts\.(?:googleapis|gstatic)\.com/.test(url)).forEach(url => errors.push(`unexpected remote link in ${file.name}: ${url}`));
    if (/@import|url\s*\(\s*['"]?https?:/i.test(file.html)) errors.push(`remote CSS dependency in ${file.name}`);
  });
  if (errors.length) fail(`verification failed:\n- ${errors.join('\n- ')}`);
  process.stdout.write(`${JSON.stringify({ ok:true, directory:path.relative(process.cwd(), outputDir), rounds:manifest.rounds.length, files:files.map(file => file.name) })}\n`);
}

function usage() {
  process.stderr.write('Usage:\n  node render.mjs --input <round.json> [--consume]\n  node render.mjs --verify <dossier-dir>\n');
  process.exitCode = 2;
}

try {
  const args = process.argv.slice(2);
  if (args[0] === '--input' && args[1]) renderInput(args[1], args.includes('--consume'));
  else if (args[0] === '--verify' && args[1]) verifyDossier(args[1]);
  else usage();
} catch (error) {
  process.stderr.write(`planning-dossier: ${error.message}\n`);
  process.exitCode = 1;
}
