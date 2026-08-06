/* AI Vitals landing page — language switch, widget/screen switchers,
   ring meter, lightbox, copy buttons. No dependencies. */
(function () {
  'use strict';

  /* ── copy ───────────────────────────────────────────────────────── */
  var I18N = {
    en: {
      'skip': 'Skip to content',
      'nav.github': 'AI Vitals on GitHub',
      'nav.kofi': 'Support on Ko-fi',
      'nav.overview': 'Overview',
      'nav.layouts': 'Widget layouts',
      'nav.screens': 'Screens',
      'nav.features': 'Features',
      'nav.privacy': 'Privacy',
      'nav.install': 'Run it',
      'nav.about': 'About me',
      'rail.note': 'Reads local provider data only.',

      'hero.eyebrow': 'WINDOWS TRAY · LOCAL-FIRST',
      'hero.title': 'Your AI quotas, always in sight.',
      'hero.sub': 'AI Vitals reads Codex and Claude Code usage from your own machine and shows it in an always-on-top widget, a tray popup, and a full dashboard. No account, no backend, no telemetry.',
      'hero.cta.code': 'Get the code on GitHub',
      'hero.cta.kofi': 'Support on Ko-fi',
      'hero.foot': 'No installer released yet — clone the repo and run it with .NET 9.',
      'hero.panel.label': 'Sample reading: Codex and Claude Code quota windows',
      'hero.panel.stamp': 'CAPTURE · 5 AUG 18:54',
      'hero.panel.available': 'AVAILABLE',
      'hero.win.week': 'Week',
      'hero.win.5h': '5 hours',
      'hero.win.resets': 'RESETS IN',
      'hero.win.clock': 'CLOCK',
      'hero.a11y.codexWeek': 'Codex, week window, 71 percent used',
      'hero.a11y.claude5h': 'Claude Code, 5-hour window, 4 percent used',
      'hero.a11y.claudeWeek': 'Claude Code, week window, 81 percent used',
      'hero.caption': 'Percentages from one capture on 5 Aug. The clocks run live, and the hairline marks how much of each window has already passed.',

      'layouts.eyebrow': 'WIDGET',
      'layouts.title': 'Three layouts. Pick the one your desk has room for.',
      'layouts.lede': 'The widget stays on top, can be locked in place or made click-through, and returns with Ctrl+Shift+U if it ends up on a monitor you unplugged. Switch layout here to see each capture at its real size.',
      'layouts.tablist': 'Widget layout',
      'layouts.tab.rings': 'Rings',
      'layouts.tab.horizontal': 'Horizontal',
      'layouts.tab.vertical': 'Vertical',
      'layouts.toggle': 'Show it on a desktop',
      'layouts.alt.rings': 'Activity-ring widget showing Codex at 71 percent and Claude at 4 and 81 percent',
      'layouts.alt.horizontal': 'Horizontal-bar widget with one bar per quota window',
      'layouts.alt.vertical': 'Vertical-bar widget, a narrow column of quota bars',
      'layouts.alt.ctx': 'The widget floating over an illustrated code editor, showing its real footprint',
      'layouts.note.rings': 'Rings: one arc per quota window, longest outside, shortest inside.',
      'layouts.note.horizontal': 'Horizontal bars: fixed 420 px width, one row per provider and window.',
      'layouts.note.vertical': 'Vertical bars: a 420 px column for a screen edge, provider icons only.',
      'layouts.ctxnote': 'Real capture placed over an illustrated workspace, at the same scale.',

      'screens.eyebrow': 'TRAY AND DASHBOARD',
      'screens.title': 'One icon in the notification area does the whole job.',
      'screens.lede': 'Left click for status you can read in a second. Right click for widget, layout and appearance controls. Double click the widget for the full dashboard with history and export.',
      'screens.tablist': 'Screen',
      'screens.tab.dashboard': 'Dashboard',
      'screens.tab.quick': 'Quick status',
      'screens.tab.tray': 'Tray controls',
      'screens.zoom': 'Open the capture at full size',
      'screens.alt.dashboard': 'AI Vitals dashboard with Codex and Claude Code quota cards',
      'screens.alt.quick': 'Quick status popup listing live quota windows',
      'screens.alt.tray': 'Tray popover with widget actions, layout and appearance controls',
      'screens.cap.dashboard': "Summary, history with provider and date filters, connections, widget preview, privacy and appearance — in the app's own dark theme.",
      'screens.cap.quick': 'Left click: live quota windows and widget shortcuts, without opening the dashboard.',
      'screens.cap.tray': 'Right click: show or hide, lock, click-through, recover, move to another display, layout and theme.',

      'features.eyebrow': 'WHAT IT DOES',
      'features.title': 'Built like an instrument, not a dashboard product.',
      'features.f1.t': 'Real provider data',
      'features.f1.d': 'Codex through its local app server, Claude Code through its local OAuth usage endpoint. Only what each provider publishes.',
      'features.f2.t': 'Honest freshness',
      'features.f2.d': 'Active, delayed, stale and expired readings are labelled as such. An absent value shows as waiting, never as 0%.',
      'features.f3.t': 'History and export',
      'features.f3.d': 'Local SQLite history with provider and date filters. Export exactly what is on screen to CSV or JSON.',
      'features.f4.t': 'Stays out of the way',
      'features.f4.d': 'Lock the widget, make it click-through, snap it to any monitor, or bring it back with Ctrl+Shift+U.',
      'features.f5.t': 'English and Spanish',
      'features.f5.d': 'Both interfaces ship in the app, with light, dark and system themes and Windows high contrast.',
      'features.f6.t': 'Readable units',
      'features.f6.d': '45.7 s, 3 min 05 s, 1,180 tokens, 1.6628 USD. Numbers are formatted for people, not for logs.',

      'privacy.eyebrow': 'PRIVACY',
      'privacy.title': 'The interesting part is what never gets written down.',
      'privacy.no.t': 'NEVER STORED',
      'privacy.no.1': 'Prompts and responses',
      'privacy.no.2': 'Transcripts and conversation content',
      'privacy.no.3': 'Project paths and session titles',
      'privacy.no.4': 'Provider credentials',
      'privacy.no.5': 'Raw session identifiers',
      'privacy.no.6': 'Analytics of any kind',
      'privacy.yes.t': 'KEPT ON YOUR MACHINE',
      'privacy.yes.1': 'Quota windows and usage counters',
      'privacy.yes.2': 'Preferences, theme, language and widget position',
      'privacy.yes.3': 'Session identifiers, pseudonymized locally with HMAC',
      'privacy.note': 'No account, no backend, no update ping. Raw provider payloads are discarded once the allowlisted fields are read.',

      'install.eyebrow': 'RUN IT',
      'install.title': 'Two commands from clone to tray.',
      'install.lede': 'Windows 10 22H2 or Windows 11, .NET SDK 9, and Codex or Claude Code signed in locally for live data. There is no packaged installer yet.',
      'install.s1': 'Clone',
      'install.s2': 'Run',
      'install.copy': 'Copy command',
      'install.copy.short': 'COPY',
      'install.copied': 'COPIED',
      'install.after': 'AI Vitals starts in the notification area. Left-click the tray icon for status, right-click for the widget and appearance controls.',

      'about.eyebrow': 'WHO BUILT THIS',
      'about.bio': 'Software engineer specialized in backend development with Java and Spring Boot, comfortable working with JavaScript and Python. I enjoy building personal projects to keep learning, with a growing interest in infrastructure.',
      'about.fact1': 'Backend Software Engineer',
      'about.fact2': 'Java, Spring Boot, JavaScript, Python',
      'about.fact3': 'Remote',

      'foot.left': 'AI Vitals · MIT License · Built with AI assistance through Codex and Claude Code.',
      'lightbox.close': 'Close'
    },

    es: {
      'skip': 'Ir al contenido',
      'nav.github': 'AI Vitals en GitHub',
      'nav.kofi': 'Apoyar en Ko-fi',
      'nav.overview': 'Resumen',
      'nav.layouts': 'Widgets',
      'nav.screens': 'Pantallas',
      'nav.features': 'Funciones',
      'nav.privacy': 'Privacidad',
      'nav.install': 'Ejecutar',
      'nav.about': 'Sobre mí',
      'rail.note': 'Solo lee datos locales de los proveedores.',

      'hero.eyebrow': 'BANDEJA DE WINDOWS · LOCAL',
      'hero.title': 'Tus cuotas de IA, siempre a la vista.',
      'hero.sub': 'AI Vitals lee el uso de Codex y Claude Code desde tu propio equipo y lo muestra en un widget siempre visible, un menú en la bandeja y un panel completo. Sin cuenta, sin servidor y sin telemetría.',
      'hero.cta.code': 'Ver el código en GitHub',
      'hero.cta.kofi': 'Apoyar en Ko-fi',
      'hero.foot': 'Todavía no hay instalador — clona el repositorio y ejecútalo con .NET 9.',
      'hero.panel.label': 'Lectura de ejemplo: ventanas de cuota de Codex y Claude Code',
      'hero.panel.stamp': 'CAPTURA · 5 AGO 18:54',
      'hero.panel.available': 'DISPONIBLE',
      'hero.win.week': 'Semana',
      'hero.win.5h': '5 horas',
      'hero.win.resets': 'SE REINICIA EN',
      'hero.win.clock': 'RELOJ',
      'hero.a11y.codexWeek': 'Codex, ventana semanal, 71 por ciento consumido',
      'hero.a11y.claude5h': 'Claude Code, ventana de 5 horas, 4 por ciento consumido',
      'hero.a11y.claudeWeek': 'Claude Code, ventana semanal, 81 por ciento consumido',
      'hero.caption': 'Porcentajes de una captura del 5 de agosto. Los relojes corren en vivo y la línea marca cuánto tiempo ha pasado de cada ventana.',

      'layouts.eyebrow': 'WIDGET',
      'layouts.title': 'Tres formatos. Elige el que te quepa en pantalla.',
      'layouts.lede': 'El widget se mantiene encima, se puede fijar o volver transparente al clic, y vuelve con Ctrl+Mayús+U si se queda en un monitor que ya no está. Cambia de formato aquí para ver cada captura a tamaño real.',
      'layouts.tablist': 'Formato del widget',
      'layouts.tab.rings': 'Anillos',
      'layouts.tab.horizontal': 'Horizontal',
      'layouts.tab.vertical': 'Vertical',
      'layouts.toggle': 'Verlo sobre un escritorio',
      'layouts.alt.rings': 'Widget de anillos con Codex al 71 por ciento y Claude al 4 y 81 por ciento',
      'layouts.alt.horizontal': 'Widget de barras horizontales, una barra por ventana de cuota',
      'layouts.alt.vertical': 'Widget de barras verticales, una columna estrecha de cuotas',
      'layouts.alt.ctx': 'El widget sobre un editor de código ilustrado, para ver el espacio que ocupa',
      'layouts.note.rings': 'Anillos: un arco por ventana de cuota, la más larga fuera y la más corta dentro.',
      'layouts.note.horizontal': 'Barras horizontales: 420 px de ancho fijo, una fila por proveedor y ventana.',
      'layouts.note.vertical': 'Barras verticales: columna de 420 px para el borde de la pantalla, solo iconos.',
      'layouts.ctxnote': 'Captura real colocada sobre un escritorio ilustrado, a la misma escala.',

      'screens.eyebrow': 'BANDEJA Y PANEL',
      'screens.title': 'Un icono en la bandeja hace todo el trabajo.',
      'screens.lede': 'Clic izquierdo para ver el estado de un vistazo. Clic derecho para el widget, el formato y la apariencia. Doble clic en el widget para abrir el panel completo con historial y exportación.',
      'screens.tablist': 'Pantalla',
      'screens.tab.dashboard': 'Panel',
      'screens.tab.quick': 'Estado rápido',
      'screens.tab.tray': 'Menú de bandeja',
      'screens.zoom': 'Abrir la captura a tamaño completo',
      'screens.alt.dashboard': 'Panel de AI Vitals con las tarjetas de cuota de Codex y Claude Code',
      'screens.alt.quick': 'Ventana de estado rápido con las ventanas de cuota activas',
      'screens.alt.tray': 'Menú de bandeja con acciones del widget, formato y apariencia',
      'screens.cap.dashboard': 'Resumen, historial con filtros por proveedor y fecha, conexiones, vista previa del widget, privacidad y apariencia, con el tema oscuro de la aplicación.',
      'screens.cap.quick': 'Clic izquierdo: cuotas en vivo y accesos del widget, sin abrir el panel.',
      'screens.cap.tray': 'Clic derecho: mostrar u ocultar, fijar, clic pasante, recuperar, mover de pantalla, formato y tema.',

      'features.eyebrow': 'QUÉ HACE',
      'features.title': 'Hecho como un instrumento, no como un producto de dashboards.',
      'features.f1.t': 'Datos reales del proveedor',
      'features.f1.d': 'Codex a través de su app server local y Claude Code a través de su endpoint local de uso OAuth. Solo lo que cada proveedor publica.',
      'features.f2.t': 'Frescura honesta',
      'features.f2.d': 'Las lecturas activas, retrasadas, obsoletas y caducadas se etiquetan como tales. Un valor ausente aparece como pendiente, nunca como 0%.',
      'features.f3.t': 'Historial y exportación',
      'features.f3.d': 'Historial local en SQLite con filtros por proveedor y fecha. Exporta a CSV o JSON justo lo que estás viendo.',
      'features.f4.t': 'No molesta',
      'features.f4.d': 'Fija el widget, hazlo pasante al clic, colócalo en cualquier monitor o recupéralo con Ctrl+Mayús+U.',
      'features.f5.t': 'Español e inglés',
      'features.f5.d': 'Las dos interfaces vienen incluidas, con temas claro, oscuro y del sistema, y alto contraste de Windows.',
      'features.f6.t': 'Unidades legibles',
      'features.f6.d': '45,7 s, 3 min 05 s, 1.180 tokens, 1,6628 USD. Los números están formateados para personas, no para logs.',

      'privacy.eyebrow': 'PRIVACIDAD',
      'privacy.title': 'Lo interesante es lo que nunca se guarda.',
      'privacy.no.t': 'NUNCA SE GUARDA',
      'privacy.no.1': 'Prompts ni respuestas',
      'privacy.no.2': 'Transcripciones ni contenido de conversaciones',
      'privacy.no.3': 'Rutas de proyectos ni títulos de sesión',
      'privacy.no.4': 'Credenciales de los proveedores',
      'privacy.no.5': 'Identificadores de sesión en bruto',
      'privacy.no.6': 'Analítica de ningún tipo',
      'privacy.yes.t': 'SE QUEDA EN TU EQUIPO',
      'privacy.yes.1': 'Ventanas de cuota y contadores de uso',
      'privacy.yes.2': 'Preferencias, tema, idioma y posición del widget',
      'privacy.yes.3': 'Identificadores de sesión, seudonimizados en local con HMAC',
      'privacy.note': 'Sin cuenta, sin servidor y sin comprobación de actualizaciones. Las respuestas del proveedor se descartan en cuanto se leen los campos permitidos.',

      'install.eyebrow': 'EJECUTAR',
      'install.title': 'Dos comandos, del clone a la bandeja.',
      'install.lede': 'Windows 10 22H2 o Windows 11, .NET SDK 9 y Codex o Claude Code con sesión iniciada en local para ver datos en vivo. Todavía no hay instalador empaquetado.',
      'install.s1': 'Clonar',
      'install.s2': 'Ejecutar',
      'install.copy': 'Copiar comando',
      'install.copy.short': 'COPIAR',
      'install.copied': 'COPIADO',
      'install.after': 'AI Vitals arranca en la bandeja del sistema. Clic izquierdo en el icono para ver el estado y clic derecho para el widget y la apariencia.',

      'about.eyebrow': 'QUIÉN LO HA HECHO',
      'about.bio': 'Software engineer especializado en backend con Java y Spring Boot, con soltura en JavaScript y Python. Disfruto creando proyectos personales para seguir aprendiendo, con un interés creciente en el mundo de la infraestructura.',
      'about.fact1': 'Backend Software Engineer',
      'about.fact2': 'Java, Spring Boot, JavaScript, Python',
      'about.fact3': 'Remoto',

      'foot.left': 'AI Vitals · Licencia MIT · Desarrollado con asistencia de IA a través de Codex y Claude Code.',
      'lightbox.close': 'Cerrar'
    }
  };

  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  /* ── language ───────────────────────────────────────────────────── */
  var lang = 'en';
  try {
    var stored = localStorage.getItem('aivitals.lang');
    if (stored === 'es' || stored === 'en') { lang = stored; }
  } catch (e) { /* storage unavailable */ }

  function t(key) {
    var pack = I18N[lang] || I18N.en;
    return pack[key] !== undefined ? pack[key] : (I18N.en[key] || '');
  }

  function applyLanguage() {
    document.documentElement.lang = lang;
    document.documentElement.setAttribute('data-lang', lang);

    $$('[data-i18n]').forEach(function (el) { el.textContent = t(el.getAttribute('data-i18n')); });
    $$('[data-i18n-label]').forEach(function (el) { el.setAttribute('aria-label', t(el.getAttribute('data-i18n-label'))); });
    $$('[data-i18n-alt]').forEach(function (el) { el.alt = t(el.getAttribute('data-i18n-alt')); });

    $$('[data-lang-set]').forEach(function (btn) {
      var on = btn.getAttribute('data-lang-set') === lang;
      btn.classList.toggle('is-on', on);
      btn.setAttribute('aria-pressed', on ? 'true' : 'false');
    });

    syncStageNote();
  }

  $$('[data-lang-set]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      lang = btn.getAttribute('data-lang-set');
      try { localStorage.setItem('aivitals.lang', lang); } catch (e) { /* ignore */ }
      applyLanguage();
    });
  });

  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ── hero panel ─────────────────────────────────────────────────────
     Percentages are frozen sample values from the capture of 5 Aug. The
     clocks are not: each window has a real reset schedule, so the panel
     counts down to the next reset and marks how far the clock has moved
     inside the window. Nothing here is dressed up as something it isn't. */
  var HOUR = 3600000;
  var WINDOWS = {
    'codex-week':  { reset: new Date(2025, 7, 9, 18, 54, 0), span: 7 * 24 * HOUR },
    'claude-5h':   { reset: new Date(2025, 7, 5, 23, 19, 0), span: 5 * HOUR },
    'claude-week': { reset: new Date(2025, 7, 6, 11, 59, 0), span: 7 * 24 * HOUR }
  };

  function nextReset(win, now) {
    var base = win.reset.getTime();
    var steps = Math.ceil((now - base) / win.span);
    if (steps < 1) { steps = 1; }
    return base + steps * win.span;
  }

  function clockText(ms) {
    var s = Math.max(0, Math.round(ms / 1000));
    var d = Math.floor(s / 86400); s -= d * 86400;
    var h = Math.floor(s / 3600); s -= h * 3600;
    var m = Math.floor(s / 60); s -= m * 60;
    var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
    return (d ? d + 'd ' : '') + pad(h) + ':' + pad(m) + ':' + pad(s);
  }

  var panelTargets = $$('[data-window]');

  function tickPanel() {
    var now = Date.now();
    var cache = {};
    panelTargets.forEach(function (el) {
      var key = el.getAttribute('data-window');
      var win = WINDOWS[key];
      if (!win) { return; }
      if (!cache[key]) {
        var left = nextReset(win, now) - now;
        cache[key] = { left: left, elapsed: Math.min(100, Math.max(0, (1 - left / win.span) * 100)) };
      }
      if (el.classList.contains('meter__now')) { el.style.left = cache[key].elapsed.toFixed(1) + '%'; }
      else if (el.tagName === 'TIME') { el.textContent = clockText(cache[key].left); }
      else { el.textContent = Math.round(cache[key].elapsed) + '%'; }
    });
  }

  if (panelTargets.length) {
    tickPanel();
    setInterval(tickPanel, 1000);
  }

  /* fill the meters once, from 0, so the panel reads as a live instrument */
  function fillMeters() {
    $$('[data-fill]').forEach(function (el) { el.style.width = el.getAttribute('data-fill') + '%'; });
  }
  if (reduced) { fillMeters(); } else { setTimeout(fillMeters, 220); }

  /* count the figures up once, in step with the meters */
  $$('[data-count]').forEach(function (el) {
    var target = parseInt(el.getAttribute('data-count'), 10);
    if (reduced) { el.textContent = target + '%'; return; }
    var start = null;
    el.textContent = '0%';
    function step(ts) {
      if (start === null) { start = ts; }
      var p = Math.min((ts - start) / 1400, 1);
      var eased = 1 - Math.pow(1 - p, 3);
      el.textContent = Math.round(target * eased) + '%';
      if (p < 1) { requestAnimationFrame(step); }
    }
    setTimeout(function () { requestAnimationFrame(step); }, 220);
  });

  /* ── widget layout switcher ─────────────────────────────────────── */
  var LAYOUTS = {
    rings: { shot: 'panel-rings', ctx: 'assets/img/widget-rings-cartoon.png', dims: '274 × 178 px' },
    horizontal: { shot: 'panel-horizontal', ctx: 'assets/img/widget-horizontal-cartoon.png', dims: '420 × 117 px' },
    vertical: { shot: 'panel-vertical', ctx: 'assets/img/widget-vertical-cartoon.png', dims: '144 × 420 px' }
  };

  var current = 'rings';
  var ctxToggle = $('#ctxToggle');
  var ctxPanel = $('#panel-ctx');
  var ctxImage = $('#ctxImage');
  var stageDims = $('#stageDims');
  var stageNote = $('#stageNote');

  function syncStageNote() {
    if (!stageNote) { return; }
    var onDesk = ctxToggle && ctxToggle.checked;
    var key = onDesk ? 'layouts.ctxnote' : 'layouts.note.' + current;
    stageNote.setAttribute('data-i18n', key);
    stageNote.textContent = t(key);
  }

  function renderStage() {
    var onDesk = ctxToggle && ctxToggle.checked;
    Object.keys(LAYOUTS).forEach(function (key) {
      var panel = document.getElementById(LAYOUTS[key].shot);
      if (!panel) { return; }
      var show = !onDesk && key === current;
      panel.hidden = !show;
      panel.classList.toggle('is-hidden', !show);
    });
    if (ctxPanel) {
      ctxPanel.hidden = !onDesk;
      ctxPanel.classList.toggle('is-hidden', !onDesk);
      if (onDesk && ctxImage) { ctxImage.src = LAYOUTS[current].ctx; }
    }
    if (stageDims) { stageDims.textContent = LAYOUTS[current].dims; }
    syncStageNote();
  }

  var layoutTabs = $$('[data-layout]');
  function selectLayout(key, focus) {
    current = key;
    layoutTabs.forEach(function (tab) {
      var on = tab.getAttribute('data-layout') === key;
      tab.setAttribute('aria-selected', on ? 'true' : 'false');
      tab.tabIndex = on ? 0 : -1;
      if (on && focus) { tab.focus(); }
    });
    renderStage();
  }

  layoutTabs.forEach(function (tab) {
    tab.addEventListener('click', function () { selectLayout(tab.getAttribute('data-layout'), false); });
  });

  if (ctxToggle) { ctxToggle.addEventListener('change', renderStage); }

  /* ── screen switcher ────────────────────────────────────────────── */
  var screenTabs = $$('[data-screen]');
  function selectScreen(key, focus) {
    screenTabs.forEach(function (tab) {
      var on = tab.getAttribute('data-screen') === key;
      tab.setAttribute('aria-selected', on ? 'true' : 'false');
      tab.tabIndex = on ? 0 : -1;
      if (on && focus) { tab.focus(); }
      var panel = document.getElementById(tab.getAttribute('aria-controls'));
      if (panel) {
        panel.hidden = !on;
        panel.classList.toggle('is-hidden', !on);
      }
    });
  }
  screenTabs.forEach(function (tab) {
    tab.addEventListener('click', function () { selectScreen(tab.getAttribute('data-screen'), false); });
  });

  /* arrow-key navigation for both tablists */
  $$('[role="tablist"]').forEach(function (list) {
    list.addEventListener('keydown', function (event) {
      if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft' && event.key !== 'Home' && event.key !== 'End') { return; }
      var tabs = $$('[role="tab"]', list);
      var index = tabs.indexOf(document.activeElement);
      if (index < 0) { return; }
      event.preventDefault();
      var next = index;
      if (event.key === 'ArrowRight') { next = (index + 1) % tabs.length; }
      if (event.key === 'ArrowLeft') { next = (index - 1 + tabs.length) % tabs.length; }
      if (event.key === 'Home') { next = 0; }
      if (event.key === 'End') { next = tabs.length - 1; }
      var target = tabs[next];
      if (target.hasAttribute('data-layout')) { selectLayout(target.getAttribute('data-layout'), true); }
      else { selectScreen(target.getAttribute('data-screen'), true); }
    });
  });

  /* ── lightbox ───────────────────────────────────────────────────── */
  var lightbox = $('#lightbox');
  var lightboxImage = $('#lightboxImage');
  if (lightbox && typeof lightbox.showModal === 'function') {
    $$('[data-zoom]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var img = $('img', btn);
        lightboxImage.src = btn.getAttribute('data-zoom');
        lightboxImage.alt = img ? img.alt : '';
        lightbox.showModal();
      });
    });
    $('.lightbox__close').addEventListener('click', function () { lightbox.close(); });
    lightbox.addEventListener('click', function (event) {
      if (event.target === lightbox) { lightbox.close(); }
    });
  }

  /* ── copy buttons ───────────────────────────────────────────────── */
  $$('[data-copy]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var text = btn.getAttribute('data-copy');
      var label = $('span', btn);
      var done = function () {
        btn.classList.add('is-done');
        if (label) { label.textContent = t('install.copied'); }
        setTimeout(function () {
          btn.classList.remove('is-done');
          if (label) { label.textContent = t('install.copy.short'); }
        }, 1600);
      };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, function () { /* denied */ });
      }
    });
  });

  /* ── portrait fallback to initials ──────────────────────────────── */
  var photo = $('#aboutPhoto');
  if (photo) {
    var fallback = function () { photo.parentElement.classList.add('is-fallback'); };
    photo.addEventListener('error', fallback);
    if (photo.complete && photo.naturalWidth === 0) { fallback(); }
  }

  /* ── rail scrollspy + reveal ────────────────────────────────────── */
  var railItems = $$('.rail__item');
  var sections = railItems.map(function (item) { return document.querySelector(item.getAttribute('href')); }).filter(Boolean);

  if ('IntersectionObserver' in window && sections.length) {
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) { return; }
        railItems.forEach(function (item) {
          item.classList.toggle('is-current', item.getAttribute('href') === '#' + entry.target.id);
        });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    sections.forEach(function (section) { spy.observe(section); });
  }

  if ('IntersectionObserver' in window && !reduced) {
    var revealables = $$('.section__head, .cards, .stage, .screens, .privacy, .steps, .about');
    revealables.forEach(function (el) { el.classList.add('reveal'); });
    var revealer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-in');
          revealer.unobserve(entry.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px' });
    revealables.forEach(function (el) { revealer.observe(el); });
  }

  applyLanguage();
  renderStage();
})();
