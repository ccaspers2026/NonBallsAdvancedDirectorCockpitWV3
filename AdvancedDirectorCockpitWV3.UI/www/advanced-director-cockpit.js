/* Advanced Director Cockpit
   Fetches data/*.json, draws the tray and tabs, then binds drag, click, and collapse.
   Edits to cards, groups, tabs, and stages are written back to those JSON files.
*/
/* Pane splits, detach, and floating sample panes. Launch Pad init runs after JSON renders. */
(function(){
  /* Converts a number to a pixel string. */
  function px(n){return n+'px'}
  /* Shows the view for a tab id and marks that tab active. */
  function activateView(name){
    if(!name) return;
    document.querySelectorAll('.tab').forEach(function(t){t.classList.toggle('active', t.dataset.view===name)});
    document.querySelectorAll('.view').forEach(function(v){v.classList.remove('active')});
    var v=document.getElementById('view-'+name);
    if(v) v.classList.add('active');
    try { localStorage.setItem('lp-active-tab', name); } catch(err) {}
    var model = window.__ADC && window.__ADC.model;
    if(model && Array.isArray(model.tabs)){
      model.tabs.forEach(function(t){ if(t) t.active = t.id === name; });
      if(typeof window.__adcSaveOverlay === 'function'){
        try { window.__adcSaveOverlay(); } catch(err) {}
      }
    }
    try { document.dispatchEvent(new CustomEvent('cockpit:tab', { detail: name })); } catch(_) {}
  }
  /* Binds each tab chip once so a click switches views and stores the active tab. */
  function bindViewTabs(){
    document.querySelectorAll('.tab[data-view]').forEach(function(tab){
      if(tab.dataset.adcViewBound) return;
      tab.dataset.adcViewBound = '1';
      function openThisTab(){
        if(window.__adcSuppressTabClick){ window.__adcSuppressTabClick = false; return; }
        activateView(tab.dataset.view);
        try { localStorage.setItem('lp-active-tab', tab.dataset.view); } catch(err) {}
      }
      tab.addEventListener('click', openThisTab);
      tab.addEventListener('keydown', function(e){
        if(e.key !== 'Enter' && e.key !== ' ') return;
        e.preventDefault();
        openThisTab();
      });
    });
  }
  bindViewTabs();
  window.__adcBindViewTabs = bindViewTabs;
  window.__activateView = activateView;
  /* 2026-10-09 v1.5.0: the agent tabs' sample-pane code (splits, float/dock, corner resize) was removed with those tabs. */
  /* ===== Launch Pad layout MVP ===== */
  /* Builds stages, tray drag, configs, and the layout toolbar after the tray exists. */
  function initLaunchPad(){
    var stageModel = (window.__ADC && window.__ADC.model && window.__ADC.model.stages) || null;
    var LAYOUTS = {};
    var DEFAULT_STAGE_SIZES = {};
    var FALLBACK_LAYOUTS = [
      { id:'single', count:1, cls:'layout-single', cols:[1], rows:[1] },
      { id:'half', count:2, cls:'layout-half', cols:[0.5,0.5], rows:[1] },
      { id:'3367', count:2, cls:'layout-3367', cols:[1,2], rows:[1] },
      { id:'three', count:3, cls:'layout-three', cols:[1,1,1], rows:[1] },
      { id:'l2', count:3, cls:'layout-l2', cols:[1.2,1], rows:[0.5,0.5] },
      { id:'l3', count:4, cls:'layout-l3', cols:[1.2,1], rows:[1,1,1] },
      { id:'l4', count:5, cls:'layout-l4', cols:[1.2,1], rows:[1,1,1,1] }
    ];
    /* Copies layout count, class, and seed track sizes into the live maps. */
    function applyStageDefs(){
      var src = (window.__ADC && window.__ADC.model && window.__ADC.model.stages && window.__ADC.model.stages.layouts) || FALLBACK_LAYOUTS;
      Object.keys(LAYOUTS).forEach(function(k){ delete LAYOUTS[k]; });
      Object.keys(DEFAULT_STAGE_SIZES).forEach(function(k){ delete DEFAULT_STAGE_SIZES[k]; });
      src.forEach(function(L){
        LAYOUTS[L.id] = { count: L.count, cls: L.cls };
        DEFAULT_STAGE_SIZES[L.id] = { cols: (L.cols || [1]).slice(), rows: (L.rows || [1]).slice() };
      });
    }
    applyStageDefs();
    var ICONS = {};
    var FALLBACK_ICONS = {
      'YNAB':'📒','Joint / reserves':'🔗',
      'CFD':'🐴',
      'BBS apex':'🌐','BBS develop':'🟢','BBS .store':'🛒','BBS develop .store':'🛒',
      'SBE apex':'⚙️','SBE www':'⚙️','SBE develop':'🟢','SBE .store':'🛒','SBE develop .store':'🛒',
      'SBE entry':'🚪','SBE entry Dev':'🚪','SBE workers':'🔧','SBE workers Dev':'🔧',
      'Cockpit Prod':'🛫','Cockpit Dev':'🛫',
      'TradingView':'📈'
    };
    /* Rebuilds stage glyphs from cards, or the built-in list when JSON did not load. */
    function refreshIcons(){
      Object.keys(ICONS).forEach(function(k){ delete ICONS[k]; });
      var cardRows = window.__ADC && window.__ADC.model && window.__ADC.model.cards;
      if(cardRows && cardRows.length){
        cardRows.forEach(function(c){ if(c.app && c.icon) ICONS[c.app] = c.icon; });
        return;
      }
      Object.keys(FALLBACK_ICONS).forEach(function(k){ ICONS[k] = FALLBACK_ICONS[k]; });
    }
    refreshIcons();

    /* ===== Per-tab stages (2026-09-28): every tab owns layout + slots + sizes ===== */
    var TAB_STAGES_KEY = 'lp-tab-stages';
    var NATIVE = (stageModel && stageModel.nativeSlot) || '@native';
    var TAB_IDS = [], TAB_LABELS = {};
    document.querySelectorAll('.tab[data-view]').forEach(function(t){
      TAB_IDS.push(t.dataset.view);
      var tabsModel = window.__ADC && window.__ADC.model && window.__ADC.model.tabs;
      var foundTab = null;
      if(tabsModel){ for(var ti=0; ti<tabsModel.length; ti++){ if(tabsModel[ti].id === t.dataset.view) foundTab = tabsModel[ti]; } }
      TAB_LABELS[t.dataset.view] = foundTab ? foundTab.label : t.textContent.trim();
    });
    var grids = {}, natives = {}, holders = {};
    var launchGrid = document.getElementById('lpStageGrid');
    if(!launchGrid) return;
    grids.launch = launchGrid;
    // Team tabs: wrap existing sample content as a movable "native view" pane, add a stage grid.
    /* Wraps one tab's markup as its native stage pane, creating an empty view when needed. */
    function ensureTabStage(tab){
      if(tab === 'launch' || grids[tab]) return;
      var view = document.getElementById('view-' + tab);
      if(!view){
        view = document.createElement('div');
        view.className = 'view';
        view.id = 'view-' + tab;
        var emptyWs = document.createElement('div');
        emptyWs.className = 'workspace';
        var emptyNote = document.createElement('p');
        emptyNote.className = 'sample-note';
        emptyNote.textContent = 'Empty tab. Drag tray tiles onto this stage.';
        emptyWs.appendChild(emptyNote);
        view.appendChild(emptyWs);
        var viewHost = document.getElementById('appMain');
        if(viewHost) viewHost.appendChild(view);
      }
      var nat = document.createElement('div');
      nat.className = 'lp-native';
      nat.dataset.nativeTab = tab;
      while(view.firstChild) nat.appendChild(view.firstChild);
      var holder = document.createElement('div');
      holder.className = 'lp-native-holder';
      holder.appendChild(nat);
      var stage = document.createElement('section');
      stage.className = 'lp-stage';
      var g = document.createElement('div');
      g.className = 'lp-stage-grid layout-single';
      g.dataset.tab = tab;
      g.setAttribute('aria-label', (TAB_LABELS[tab] || tab) + ' stage');
      stage.appendChild(g);
      view.appendChild(stage);
      view.appendChild(holder);
      grids[tab] = g; natives[tab] = nat; holders[tab] = holder;
        }
    /* Removes a non-launch tab's stage from the DOM. */
    function removeTabStage(tab){
      if(tab === 'launch'){ /* v1.5.0: the first setup can be deleted; its grid element stays, empty and hidden */ var lv = document.getElementById('view-launch'); if(lv) lv.classList.remove('active'); if(grids.launch) grids.launch.innerHTML = ''; delete stages.launch; var li = TAB_IDS.indexOf('launch'); if(li >= 0) TAB_IDS.splice(li, 1); delete TAB_LABELS.launch; persistStages(); return true; }
      var gone = document.getElementById('view-' + tab);
      if(gone) gone.remove();
      delete grids[tab];
      delete natives[tab];
      delete holders[tab];
      delete stages[tab];
      var rm = TAB_IDS.indexOf(tab);
      if(rm >= 0) TAB_IDS.splice(rm, 1);
      delete TAB_LABELS[tab];
      if(stageTab === tab && typeof window.__activateView === 'function'){
        var nextId = '';
        var tabsLeft = window.__ADC && window.__ADC.model && window.__ADC.model.tabs;
        if(tabsLeft && tabsLeft.length){
          for(var ti=0; ti<tabsLeft.length; ti++){
            if(tabsLeft[ti] && tabsLeft[ti].id === 'launch') nextId = 'launch';
          }
          if(!nextId && tabsLeft[0] && tabsLeft[0].id) nextId = tabsLeft[0].id;
        }
        if(nextId) window.__activateView(nextId);
      }
      persistStages();
      return true;
    }
    TAB_IDS.forEach(function(tab){
      if(tab === 'launch') return;
      ensureTabStage(tab);
    });

    /* Returns the seed layout and slots for a tab. */
    function defaultStage(tab){
      var defs = window.__ADC && window.__ADC.model && window.__ADC.model.stages && window.__ADC.model.stages.defaults;
      var spec = null;
      if(defs){
        if(tab === 'launch' && defs.launch) spec = defs.launch;
        else if(defs.other) spec = defs.other;
      }
      if(!spec){
        spec = tab === 'launch'
          ? { layout:'half', slots:['YNAB'] }
          : { layout:'single', slots:[NATIVE] };
      }
      return { layout: spec.layout, slots: (spec.slots || []).slice(), sizes: {}, loads: [] };
    }
    /* Fills missing layout, slots, and sizes on a saved stage. */
    function normStage(tab, s){
      var d = defaultStage(tab);
      var hadSlots = !!(s && typeof s === 'object' && Array.isArray(s.slots));
      if(!s || typeof s !== 'object') s = d;
      var slots = Array.isArray(s.slots) ? s.slots.slice() : d.slots.slice();
      if(!natives[tab]) slots = slots.map(function(a){ return a === NATIVE ? null : a; });
      slots = slots.map(function(a){ var k = String(a == null ? '' : a).toLowerCase(); return (k === 'bill calendar' || k === 'statements' || k === 'failed payments' || k === 'subscriptions' || k === 'accounts' || k === 'exports' || k === 'thinkorswim' || k === 'trade journal' || k === 'positions') ? null : a; });
      var loads = Array.isArray(s.loads) ? s.loads.slice() : null;
      if(!loads){
        var fromModel = window.__ADC && window.__ADC.model && window.__ADC.model.stages && window.__ADC.model.stages.loads;
        if(fromModel && Array.isArray(fromModel[tab])) loads = fromModel[tab].slice();
      }
      if(!loads) loads = [];
      while(loads.length < slots.length) loads.push(null);
      loads = loads.slice(0, slots.length).map(function(x, i){
        if(slots[i] == null || slots[i] === NATIVE) return null;
        if(x && typeof x === 'object') return { cardId: x.cardId || '', url: x.url ? String(x.url) : '' };
        return null;
      });
      var layoutId = LAYOUTS[s.layout] ? s.layout : d.layout;
      if(!hadSlots){
        var seed = LAYOUTS[layoutId];
        var need = seed && seed.count ? seed.count : 1;
        while(slots.length < need) slots.push(null);
      }
      if(!slots.length) slots.push(null);
      return {
        layout: layoutId,
        slots: slots,
        sizes: (s.sizes && typeof s.sizes === 'object') ? s.sizes : {},
        loads: loads,
        activeSlot: null,
        drag: null
      };
    }
    /* Writes every tab's layout and slots to localStorage. */
    function persistStages(){
      var out = {};
      var pack = {};
      Object.keys(stages).forEach(function(t){
        var st = stages[t];
        var loads = (st.loads || []).map(function(x){
          if(!x || typeof x !== 'object') return null;
          return { cardId: x.cardId || '', url: x.url || '' };
        });
        out[t] = { layout: st.layout, slots: st.slots.slice(), sizes: st.sizes, loads: loads };
        pack[t] = loads;
      });
      var model = window.__ADC && window.__ADC.model;
      if(model && model.stages && typeof model.stages === 'object') model.stages.loads = pack;
      try { localStorage.setItem(TAB_STAGES_KEY, JSON.stringify(out)); } catch(_) {}
    }
    var stages = {};
    /* Reads saved stages, including the older single-stage key. */
    (function loadStages(){
      var raw = null;
      try { raw = JSON.parse(localStorage.getItem(TAB_STAGES_KEY) || 'null'); } catch(_) { raw = null; }
      var migrated = false;
      if(!raw || typeof raw !== 'object'){
        // Migrate the old single Launch Pad state (+ global lp-stage-sizes) into the launch tab.
        raw = {};
        migrated = true;
        var legacy = null;
        try { legacy = JSON.parse(localStorage.getItem('lp-stage-sizes') || 'null'); } catch(_) {}
        raw.launch = { layout:'half', slots:['YNAB'], sizes: (legacy && typeof legacy === 'object') ? legacy : {} };
      }
      Object.keys(grids).forEach(function(tab){ stages[tab] = normStage(tab, raw[tab]); });
      if(migrated) persistStages();
    })();
    var stageTab = 'launch';
    var state = stages.launch;
    var grid = launchGrid;
    var toastEl = document.getElementById('lpToast');
    var toastTimer = null;
    var lpShell = document.getElementById('appBody') || document.querySelector('.app-body');
    var lpTray = document.getElementById('lpTray') || document.querySelector('.lp-tray');
    var lpSplitter = document.getElementById('lpTraySplitter');
    var DEFAULT_TRAY_WIDTH = 300;
    var MIN_TRAY_WIDTH = 8;
    var COLLAPSED_KEY = 'lp-tray-collapsed';
    var trayCollapsed = false;
    if(!grid) return;

    /* Computes the min and max tray width for the current shell. */
    function trayBounds(){
      var shellWidth = lpShell ? lpShell.getBoundingClientRect().width : 0;
      if(!(shellWidth > 0)) return { min: MIN_TRAY_WIDTH, max: 100000 };
      var max = Math.max(MIN_TRAY_WIDTH, shellWidth - 6);
      return { min: MIN_TRAY_WIDTH, max: max };
    }
    /* Limits a tray width to the current bounds. */
    function clampTrayWidth(width){
      var bounds = trayBounds();
      return Math.max(bounds.min, Math.min(bounds.max, width));
    }
    /* Applies a tray width and optionally stores it. */
    function setTrayWidth(width, persist){
      if(!lpShell || !lpTray) return;
      if(trayCollapsed) return;
      var next = clampTrayWidth(Number(width));
      lpShell.style.setProperty('--lp-tray-width', next + 'px');
      if(lpSplitter) lpSplitter.setAttribute('aria-valuenow', String(Math.round(next)));
      if(persist){
        try { localStorage.setItem('lp-tray-width', String(Math.round(next))); } catch(_) {}
      }
    }
    /* Restores the saved tray width, or the default. */
    function restoreTrayWidth(){
      var saved = null;
      try { saved = localStorage.getItem('lp-tray-width'); } catch(_) {}
      if(saved == null){
        try { saved = sessionStorage.getItem('lp-tray-width'); } catch(_) {}
        if(saved != null){
          try { localStorage.setItem('lp-tray-width', saved); } catch(_) {}
        }
      }
      var width = saved != null && isFinite(Number(saved)) ? Number(saved) : DEFAULT_TRAY_WIDTH;
      setTrayWidth(width, false);
    }
    /* Collapses or expands the tray and optionally stores that. */
    function setTrayCollapsed(collapsed, persist){
      trayCollapsed = !!collapsed;
      if(lpShell) lpShell.classList.toggle('tray-collapsed', trayCollapsed);
      document.body.classList.toggle('tray-collapsed', trayCollapsed);
      var rail = lpTray && lpTray.querySelector('.lp-tray-rail');
      if(rail) rail.setAttribute('aria-hidden', trayCollapsed ? 'false' : 'true');
      var collapseBtn = document.getElementById('lpTrayCollapse');
      if(collapseBtn){
        collapseBtn.setAttribute('aria-expanded', trayCollapsed ? 'false' : 'true');
        collapseBtn.title = trayCollapsed ? 'Expand Launch Pad tray' : 'Collapse Launch Pad tray';
        collapseBtn.setAttribute('aria-label', collapseBtn.title);
      }
      if(lpSplitter){
        lpSplitter.setAttribute('aria-hidden', trayCollapsed ? 'true' : 'false');
        lpSplitter.tabIndex = trayCollapsed ? -1 : 0;
      }
      if(persist){
        try { localStorage.setItem(COLLAPSED_KEY, trayCollapsed ? 'true' : 'false'); } catch(_) {}
      }
      if(trayCollapsed){
        // Drop inline width so collapsed rail CSS (--lp-rail-width) wins cleanly.
        if(lpShell) lpShell.style.removeProperty('--lp-tray-width');
      } else {
        restoreTrayWidth();
      }
      // Stage gutters need a reflow after width change
      setTimeout(function(){ try { refreshStageGutters(); } catch(_){} }, 40);
    }
    /* Applies the saved tray collapsed flag. */
    function restoreTrayCollapsed(){
      var saved = null;
      try { saved = localStorage.getItem(COLLAPSED_KEY); } catch(_) {}
      setTrayCollapsed(saved === 'true', false);
    }
    if(lpShell && lpTray && lpSplitter){
      restoreTrayCollapsed();
      if(!trayCollapsed) restoreTrayWidth();
      requestAnimationFrame(function(){ if(!trayCollapsed) restoreTrayWidth(); });
      var collapseBtn = document.getElementById('lpTrayCollapse');
      var expandBtn = document.getElementById('lpTrayExpand');
      if(collapseBtn) collapseBtn.addEventListener('click', function(){ setTrayCollapsed(true, true); });
      if(expandBtn) expandBtn.addEventListener('click', function(){ setTrayCollapsed(false, true); });
      if(lpTray.querySelector('.rail-mark')){
        lpTray.querySelector('.rail-mark').addEventListener('click', function(){ setTrayCollapsed(false, true); });
      }
      lpSplitter.addEventListener('pointerdown', function(e){
        if(trayCollapsed) return;
        if(e.button !== 0) return;
        e.preventDefault();
        lpSplitter.classList.add('dragging');
        lpSplitter.setPointerCapture(e.pointerId);
        var startX = e.clientX;
        var startWidth = lpTray.getBoundingClientRect().width;
        /* Updates tray width while the splitter is dragged. */
        function move(ev){ setTrayWidth(startWidth + (ev.clientX - startX), true); }
        /* Ends a tray-width drag. */
        function finish(){
          lpSplitter.classList.remove('dragging');
          try { lpSplitter.releasePointerCapture(e.pointerId); } catch(_) {}
          lpSplitter.removeEventListener('pointermove', move);
          lpSplitter.removeEventListener('pointerup', finish);
          lpSplitter.removeEventListener('pointercancel', finish);
        }
        lpSplitter.addEventListener('pointermove', move);
        lpSplitter.addEventListener('pointerup', finish);
        lpSplitter.addEventListener('pointercancel', finish);
      });
      lpSplitter.addEventListener('dblclick', function(){
        if(trayCollapsed) return;
        setTrayWidth(DEFAULT_TRAY_WIDTH, true);
      });
      lpSplitter.addEventListener('keydown', function(e){
        if(trayCollapsed) return;
        var current = lpTray.getBoundingClientRect().width;
        if(e.key === 'ArrowLeft' || e.key === 'ArrowRight'){
          e.preventDefault();
          setTrayWidth(current + (e.key === 'ArrowLeft' ? -20 : 20), true);
        } else if(e.key === 'Home'){
          e.preventDefault();
          setTrayWidth(DEFAULT_TRAY_WIDTH, true);
        }
      });
      window.addEventListener('resize', function(){
        if(trayCollapsed) return;
        var current = lpTray.getBoundingClientRect().width;
        setTrayWidth(current, false);
      });
    }


    /* ===== Stage pane gutters (resize + localStorage) ===== */
    var STAGE_SIZES_KEY = 'lp-stage-sizes';
    var MIN_PANE_PX = 90;
    var STAGE_PAD = 10;
    var STAGE_GAP = 8;
    var stageSizesCache = null;
    var stageDrag = null;

    /* Returns the active tab's saved track sizes. */
    function loadAllStageSizes(){
      // Per-tab: sizes live on the active tab's stage (lp-tab-stages).
      if(!state.sizes || typeof state.sizes !== 'object') state.sizes = {};
      return state.sizes;
    }
    /* Stores track sizes on the active tab's stage. */
    function persistAllStageSizes(){ persistStages(); }
    /* Copies column and row track arrays. */
    function cloneSizes(s){
      return { cols: (s.cols || [1]).slice(), rows: (s.rows || [1]).slice() };
    }
    /* Returns the seed tracks for a layout id. */
    function defaultSizesFor(layout){
      return cloneSizes(DEFAULT_STAGE_SIZES[layout] || DEFAULT_STAGE_SIZES.half || { cols:[1], rows:[1] });
    }
    /* Returns saved tracks for a layout, or the seed tracks. */
    function getSizesFor(layout){
      var all = loadAllStageSizes();
      var saved = all[layout];
      if(saved && Array.isArray(saved.cols) && saved.cols.length){
        var d = defaultSizesFor(layout);
        var out = {
          cols: saved.cols.map(Number).filter(function(n){ return isFinite(n) && n > 0; }),
          rows: (saved.rows && saved.rows.length ? saved.rows : d.rows).map(Number).filter(function(n){ return isFinite(n) && n > 0; })
        };
        if(out.cols.length && out.rows.length) return out;
      }
      return defaultSizesFor(layout);
    }
    /* Keeps the weights already saved and fits the track count to the live panes. */
    function fitTracks(arr, need){
      var out = arr.slice(0, need);
      while(out.length < need) out.push(1);
      return out;
    }
    function tracksForCurrentSlots(){
      var layout = state.layout;
      var d = defaultSizesFor(layout);
      var saved = getSizesFor(layout);
      var isL = /^l[234]$/.test(layout);
      var slotCount = Math.max(1, state.slots.length);
      var sizes;
      if(isL){
        sizes = {
          cols: saved.cols.length === d.cols.length ? saved.cols.slice() : d.cols.slice(),
          rows: fitTracks(saved.rows, Math.max(1, slotCount - 1))
        };
      } else if(d.rows.length === 1){
        sizes = { cols: fitTracks(saved.cols, slotCount), rows: d.rows.slice() };
      } else {
        var meta = LAYOUTS[layout];
        var base = meta && meta.count ? meta.count : slotCount;
        var extra = Math.max(0, slotCount - base);
        sizes = {
          cols: fitTracks(saved.cols, d.cols.length + extra),
          rows: saved.rows.length === d.rows.length ? saved.rows.slice() : d.rows.slice()
        };
      }
      var same = sizes.cols.length === saved.cols.length && sizes.rows.length === saved.rows.length;
      if(same){
        for(var i = 0; i < sizes.cols.length; i++){ if(sizes.cols[i] !== saved.cols[i]) same = false; }
        for(var j = 0; j < sizes.rows.length; j++){ if(sizes.rows[j] !== saved.rows[j]) same = false; }
      }
      if(!same) setSizesFor(layout, sizes, false);
      return sizes;
    }
    /* Stores tracks for a layout and optionally persists them. */
    function setSizesFor(layout, sizes, persist){
      var all = loadAllStageSizes();
      all[layout] = { cols: sizes.cols.slice(), rows: sizes.rows.slice() };
      if(persist) persistAllStageSizes();
    }
    /* Joins track weights into a CSS grid template. */
    function frTemplate(arr){
      return arr.map(function(f){ return f + 'fr'; }).join(' ');
    }
    /* Adds a list of numbers. */
    function sum(arr){
      return arr.reduce(function(a,b){ return a + b; }, 0);
    }
    /* Removes gutter handles from the stage grid. */
    function clearStageGutters(){
      grid.querySelectorAll('.lp-gutter').forEach(function(g){ g.parentNode.removeChild(g); });
    }
    /* Writes column and row templates onto the grid. */
    function applyStageTrackSizes(sizes){
      grid.style.gridTemplateColumns = frTemplate(sizes.cols);
      grid.style.gridTemplateRows = frTemplate(sizes.rows);
      scheduleSlotReport();
    }
    /* Measures where the stage gutters should sit. */
    function gutterGeom(sizes){
      var rect = grid.getBoundingClientRect();
      var innerW = Math.max(0, rect.width - STAGE_PAD * 2);
      var innerH = Math.max(0, rect.height - STAGE_PAD * 2);
      var colGaps = Math.max(0, sizes.cols.length - 1) * STAGE_GAP;
      var rowGaps = Math.max(0, sizes.rows.length - 1) * STAGE_GAP;
      var availW = Math.max(0, innerW - colGaps);
      var availH = Math.max(0, innerH - rowGaps);
      var totalCol = sum(sizes.cols) || 1;
      var totalRow = sum(sizes.rows) || 1;
      var isL = /^l[234]$/.test(state.layout);
      var verts = [];
      var x = STAGE_PAD;
      for(var ci = 0; ci < sizes.cols.length - 1; ci++){
        x += (sizes.cols[ci] / totalCol) * availW;
        verts.push({ index: ci, left: x + STAGE_GAP / 2 - 3, top: STAGE_PAD, height: Math.max(0, innerH) });
        x += STAGE_GAP;
      }
      var horz = [];
      if(isL && sizes.rows.length > 1){
        var leftW = (sizes.cols[0] / totalCol) * availW;
        var rightLeft = STAGE_PAD + leftW + STAGE_GAP;
        var rightWidth = Math.max(0, STAGE_PAD + innerW - rightLeft);
        var y = STAGE_PAD;
        for(var ri = 0; ri < sizes.rows.length - 1; ri++){
          y += (sizes.rows[ri] / totalRow) * availH;
          horz.push({ index: ri, top: y + STAGE_GAP / 2 - 3, left: rightLeft, width: rightWidth });
          y += STAGE_GAP;
        }
      }
      return { verts: verts, horz: horz, availW: availW, availH: availH, rect: rect };
    }
    /* Creates the stage gutter handles. */
    function placeStageGutters(sizes){
      clearStageGutters();
      if(sizes.cols.length < 2 && sizes.rows.length < 2) return;
      var geom = gutterGeom(sizes);
      geom.verts.forEach(function(v){
        var vg = document.createElement('div');
        vg.className = 'lp-gutter lp-gutter-v';
        vg.setAttribute('role', 'separator');
        vg.setAttribute('aria-orientation', 'vertical');
        vg.setAttribute('aria-label', 'Resize columns');
        vg.tabIndex = 0;
        vg.dataset.axis = 'cols';
        vg.dataset.index = String(v.index);
        vg.style.left = v.left + 'px';
        vg.style.top = v.top + 'px';
        vg.style.height = v.height + 'px';
        bindStageGutter(vg);
        grid.appendChild(vg);
      });
      geom.horz.forEach(function(h){
        var hg = document.createElement('div');
        hg.className = 'lp-gutter lp-gutter-h';
        hg.setAttribute('role', 'separator');
        hg.setAttribute('aria-orientation', 'horizontal');
        hg.setAttribute('aria-label', 'Resize rows');
        hg.tabIndex = 0;
        hg.dataset.axis = 'rows';
        hg.dataset.index = String(h.index);
        hg.style.top = h.top + 'px';
        hg.style.left = h.left + 'px';
        hg.style.width = h.width + 'px';
        bindStageGutter(hg);
        grid.appendChild(hg);
      });
    }
    /* Repositions gutters without recreating them. */
    function syncGutterPositions(sizes){
      var geom = gutterGeom(sizes);
      geom.verts.forEach(function(v){
        var el = grid.querySelector('.lp-gutter-v[data-index="'+v.index+'"]');
        if(!el) return;
        el.style.left = v.left + 'px';
        el.style.top = v.top + 'px';
        el.style.height = v.height + 'px';
      });
      geom.horz.forEach(function(h){
        var el = grid.querySelector('.lp-gutter-h[data-index="'+h.index+'"]');
        if(!el) return;
        el.style.top = h.top + 'px';
        el.style.left = h.left + 'px';
        el.style.width = h.width + 'px';
      });
    }
    /* Rebuilds gutter handles for the current layout. */
    function refreshStageGutters(){
      var sizes = tracksForCurrentSlots();
      applyStageTrackSizes(sizes);
      placeStageGutters(sizes);
    }
    /* Shifts weight between two neighboring tracks. */
    function redistributePair(arr, index, deltaFr, minFr){
      var a = arr[index];
      var b = arr[index + 1];
      var na = a + deltaFr;
      var nb = b - deltaFr;
      if(na < minFr){ nb -= (minFr - na); na = minFr; }
      if(nb < minFr){ na -= (minFr - nb); nb = minFr; }
      if(na < minFr) na = minFr;
      if(nb < minFr) nb = minFr;
      arr[index] = na;
      arr[index + 1] = nb;
    }
    /* Binds drag and keyboard resize on one gutter. */
    function bindStageGutter(el){
      el.addEventListener('pointerdown', function(e){
        if(e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        var axis = el.dataset.axis;
        var index = parseInt(el.dataset.index, 10);
        var startSizes = cloneSizes(getSizesFor(state.layout));
        var rect = grid.getBoundingClientRect();
        stageDrag = {
          el: el,
          axis: axis,
          index: index,
          startSizes: startSizes,
          startX: e.clientX,
          startY: e.clientY,
          rect: rect,
          liveSizes: cloneSizes(startSizes)
        };
        el.classList.add('dragging');
        try { el.setPointerCapture(e.pointerId); } catch(_) {}
        /* Applies a gutter drag. */
        function onMove(ev){
          if(!stageDrag) return;
          var sizes = cloneSizes(stageDrag.startSizes);
          var colGaps = Math.max(0, sizes.cols.length - 1) * STAGE_GAP;
          var rowGaps = Math.max(0, sizes.rows.length - 1) * STAGE_GAP;
          var availW = Math.max(1, stageDrag.rect.width - STAGE_PAD * 2 - colGaps);
          var availH = Math.max(1, stageDrag.rect.height - STAGE_PAD * 2 - rowGaps);
          if(stageDrag.axis === 'cols'){
            var totalCol = sum(stageDrag.startSizes.cols) || 1;
            var pxPerFr = availW / totalCol;
            var minFr = MIN_PANE_PX / Math.max(pxPerFr, 0.0001);
            redistributePair(sizes.cols, stageDrag.index, (ev.clientX - stageDrag.startX) / pxPerFr, minFr);
          } else {
            var totalRow = sum(stageDrag.startSizes.rows) || 1;
            var pxPerFrR = availH / totalRow;
            var minFrR = MIN_PANE_PX / Math.max(pxPerFrR, 0.0001);
            redistributePair(sizes.rows, stageDrag.index, (ev.clientY - stageDrag.startY) / pxPerFrR, minFrR);
          }
          applyStageTrackSizes(sizes);
          syncGutterPositions(sizes);
          stageDrag.liveSizes = sizes;
        }
        /* Ends a gutter drag. */
        function onUp(){
          if(!stageDrag) return;
          var finalSizes = stageDrag.liveSizes || getSizesFor(state.layout);
          setSizesFor(state.layout, finalSizes, true);
          var dragEl = stageDrag.el;
          stageDrag = null;
          if(dragEl) dragEl.classList.remove('dragging');
          window.removeEventListener('pointermove', onMove);
          window.removeEventListener('pointerup', onUp);
          window.removeEventListener('pointercancel', onUp);
          applyStageTrackSizes(finalSizes);
          placeStageGutters(finalSizes);
        }
        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('pointercancel', onUp);
      });
      el.addEventListener('keydown', function(e){
        var axis = el.dataset.axis;
        var index = parseInt(el.dataset.index, 10);
        var sizes = cloneSizes(getSizesFor(state.layout));
        var step = 0.05 * (e.shiftKey ? 3 : 1);
        var handled = false;
        if(axis === 'cols' && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')){
          handled = true;
          redistributePair(sizes.cols, index, e.key === 'ArrowRight' ? step : -step, 0.08);
        } else if(axis === 'rows' && (e.key === 'ArrowUp' || e.key === 'ArrowDown')){
          handled = true;
          redistributePair(sizes.rows, index, e.key === 'ArrowDown' ? step : -step, 0.08);
        }
        if(!handled) return;
        e.preventDefault();
        setSizesFor(state.layout, sizes, true);
        applyStageTrackSizes(sizes);
        placeStageGutters(sizes);
        var sel = '.lp-gutter[data-axis="'+axis+'"][data-index="'+index+'"]';
        var g2 = grid.querySelector(sel);
        if(g2) g2.focus();
      });
    }

    /* Shows a short status message. */
    function toast(msg){
      if(!toastEl) return;
      toastEl.textContent = msg;
      toastEl.classList.add('show');
      clearTimeout(toastTimer);
      toastTimer = setTimeout(function(){ toastEl.classList.remove('show'); }, 1800);
    }


    /* Finds the tray card a stage slot is showing. */
    /* 2026-11-02 newcard fix: exact card id lookup; used before cardByApp so shared app keys cannot collide. */
    function cardById(id){
      if(!id) return null;
      var list = (window.__ADC && window.__ADC.model && window.__ADC.model.cards) || [];
      for(var i = 0; i < list.length; i++){ if(list[i] && list[i].id === id) return list[i]; }
      return null;
    }
    function cardByApp(app){
      var list = (window.__ADC && window.__ADC.model && window.__ADC.model.cards) || [];
      for(var i = 0; i < list.length; i++){
        var c = list[i];
        if(c && (c.app === app || c.id === app || c.name === app)) return c;
      }
      return null;
    }
    /* Keeps the per-slot load list lined up with slots. */
    function ensureLoads(){
      if(!Array.isArray(state.loads)) state.loads = [];
      while(state.loads.length < state.slots.length) state.loads.push(null);
      if(state.loads.length > state.slots.length) state.loads = state.loads.slice(0, state.slots.length);
    }
    /* Opens the pane iframe URL in its own window. Called from the Detach click (user gesture), never from a timer. */
    function openDetachedPane(url, nameKey){
      var safe = String(nameKey || 'pane').replace(/[^A-Za-z0-9_-]/g, '_');
      var features = 'popup=yes,width=1200,height=800,left=80,top=60,resizable=yes,scrollbars=yes';
      var w = null;
      try { w = window.open(url, 'adc-detach-' + safe, features); } catch(_){ w = null; }
      if(!w || w.closed) return null;
      return w;
    }

    /* The page a slot should show: the card's current url, else the url saved at drop. */
    function resolvePane(idx, app){
      var savedLoad = state.loads && state.loads[idx];
      var card = cardById(savedLoad && typeof savedLoad === 'object' ? savedLoad.cardId : '') || cardByApp(app);
      if(card) return { cardId: card.id || '', url: card.url ? String(card.url) : '' };
      var saved = state.loads && state.loads[idx];
      if(saved && typeof saved === 'object') return { cardId: saved.cardId || '', url: saved.url ? String(saved.url) : '' };
      return { cardId: '', url: '' };
    }
    /* Fills a stage pane with the card page, or a note when the card has no url. */
    /* 2026-10-09 vault: which card a pane shows (read by adc-vault.js). No secrets. */
    window.__adcPaneInfo = function(i){
      var a = state && state.slots ? state.slots[i] : null;
      if(!a || a === NATIVE) return null;
      var r = resolvePane(i, a) || {};
      return { cardId: String(r.cardId || ''), url: String(r.url || ''), name: String(a) };
    };
    function discardFrame(frame){
      if(!frame) return;
      try { frame.src = 'about:blank'; } catch(_){}
      if(frame.parentNode) frame.parentNode.removeChild(frame);
    }
    /* Fills a stage pane with the card page, or a note when the card has no url.
       The old iframe is blanked and removed first. A new frame is inserted, then
       navigated, so a filled window view cannot keep showing the previous site. */

    /* Pane pages are not loaded through the frame proxy in this copy.
       postPaneToHost sends the card name and the real url to the WebView2 host.
       frameProxySrc remains unused so nothing navigates an iframe to /frame/. */
    function frameProxySrc(realUrl){
      var raw = String(realUrl || '').replace(/^\s+|\s+$/g, '');
      var base = 'https://advanceddirectorcockpit.local:8080/frame/s/';
      try {
        var u = new URL(raw);
        var host = String(u.hostname || '').toLowerCase();
        if(u.port) host += ':' + u.port;
        return base + host + (u.pathname || '/') + u.search + u.hash;
      } catch(err){
        return base + encodeURIComponent(raw);
      }
    }

    var wv2Posted = Object.create(null);
    /* adc-gtabs: messages for a tab shown in another window are not sent from this one. */
    function hostSpaceOk(){ return typeof window.__adcHostSpaceOk !== 'function' || window.__adcHostSpaceOk(stageTab); }
    /* Pane title-bar move: the host swaps the two panes' web views and programs (no reload, no restart). */
    function movePaneOnHost(from, to){
      delete wv2Posted[from]; delete wv2Posted[to];
      if(!hostSpaceOk()) return;
      try { if(window.chrome && window.chrome.webview) window.chrome.webview.postMessage({ type: 'adc-move-pane', space: stageTab, from: from, to: to }); } catch(_){}
    }
    function postPaneToHost(idx, app, url){
      var clean = String(url || "").replace(/^\s+|\s+$/g, "");
      if(!clean) return;
      var sig = String(idx) + "\n" + String(app || "") + "\n" + clean;
      if(!hostSpaceOk()) return;
      if(wv2Posted[idx] === sig) return;
      wv2Posted[idx] = sig;
      try {
        if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function"){
          window.chrome.webview.postMessage({
            type: "adc-open-pane",
            space: stageTab,
            slot: idx,
            name: String(app || ""),
            url: clean
          });
        }
      } catch(postErr){}
    }

    function fillPaneBody(el, app, idx){
      var info = resolvePane(idx, app);
      ensureLoads();
      state.loads[idx] = info;
      var stale = el.querySelectorAll('iframe');
      for(var s = 0; s < stale.length; s++) discardFrame(stale[s]);
      while(el.firstChild) el.removeChild(el.firstChild);
      if(!info.url){
        el.className = 'lp-pane-b';
        var note = document.createElement('div');
        note.className = 'sample-block adc-no-page';
        var b = document.createElement('b');
        b.textContent = ((ICONS[app] || '') + ' ' + app).replace(/^\s+/, '');
        note.appendChild(b);
        note.appendChild(document.createTextNode('This card has no page yet.'));
        el.appendChild(note);
        return;
      }
      // A desktop app card (local .exe or app link) runs inside this pane: the WV2 host starts the
      // program and reparents its window onto this pane body. It is never loaded as a web page.
      var desktopUrl = desktopTarget(info.url);
      if(desktopUrl){
        el.className = 'lp-pane-b adc-embed-pane';
        var deskCard = cardById(info.cardId) || cardByApp(app);
        var deskName = (deskCard && deskCard.name) || app;
        var deskWait = document.createElement('div');
        deskWait.className = 'ph-small adc-embed-wait';
        deskWait.textContent = 'Starting ' + deskName + ' in this pane...';
        el.appendChild(deskWait);
        try {
          if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function' && hostSpaceOk()){
            window.chrome.webview.postMessage({ type: 'adc-embed-app', space: stageTab, slot: idx, name: String(deskName), cardId: (deskCard && deskCard.id) || info.cardId || '', url: desktopUrl, args: (deskCard && deskCard.args) || '', profile: (deskCard && deskCard.profile) || '' });
          }
        } catch(_){}
        return;
      }
      el.className = 'lp-pane-b';
      var note = document.createElement('div');
      note.className = 'sample-block';
      var strong = document.createElement('b');
      strong.textContent = app;
      note.appendChild(strong);
      note.appendChild(document.createTextNode('Open in the WebView2 pane.'));
      var small = document.createElement('div');
      small.className = 'ph-small';
      small.textContent = info.url;
      note.appendChild(small);
      el.appendChild(note);
      postPaneToHost(idx, app, info.url);
    }
    /* Writes the working model, including loaded pane urls, into adc-page-data. */
    function writePageOverlay(){
      var m = window.__ADC && window.__ADC.model;
      if(!m || !Array.isArray(m.cards) || !Array.isArray(m.groups) || !Array.isArray(m.tabs) || !m.stages) return;
      if(typeof window.__adcPagePayload !== "function") return;
      try {
        localStorage.setItem('adc-page-data', JSON.stringify(window.__adcPagePayload(m)));
      } catch(e){}
    }

    /* Builds the placeholder body for an app dropped on a stage. */
    function sampleBody(app){
      var ico = ICONS[app] || '▦';
      return '<div class="sample-block"><b>'+ico+' '+esc(app)+'</b>Sample / iframe later<br><span class="ph-small">Placeholder content for Launch Pad stage</span></div>';
    }
    /* Escapes text for HTML. */
    function esc(s){
      return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    /* Tells the host where each visible stage slot is, in viewport pixels.
       Slot 0 is the left slot. The host moves that slot's WebView2 onto this rectangle. */
    function scheduleSlotReport(){
      if(scheduleSlotReport._t) cancelAnimationFrame(scheduleSlotReport._t);
      scheduleSlotReport._t = requestAnimationFrame(function(){
        scheduleSlotReport._t = requestAnimationFrame(function(){
          scheduleSlotReport._t = 0;
          reportSlotBounds();
        });
      });
    }
    function reportSlotBounds(){
      var host = window.chrome && window.chrome.webview;
      if(!grid || !state || !host || typeof host.postMessage !== "function") return;
      if(!hostSpaceOk()) return;
      var meta = LAYOUTS[state.layout] || LAYOUTS.half || { count: 0 };
      var nodes = grid.querySelectorAll(".lp-slot");
      var slots = [];
      for(var i = 0; i < nodes.length; i++){
        var slotEl = nodes[i];
        var idx = parseInt(slotEl.getAttribute("data-slot"), 10);
        if(isNaN(idx)) idx = i;
        var app = state.slots && state.slots[idx];
        var nativeSlot = app === NATIVE;
        var body = slotEl.querySelector("[data-pane-body]");
        var load = state.loads && state.loads[idx];
        var pageUrl = (!nativeSlot && load && typeof load === "object" && load.url) ? String(load.url) : "";
        var filled = !!app && !nativeSlot && !!pageUrl && !!body;
        var box = filled ? body : slotEl;
        var r = box.getBoundingClientRect();
        slots.push({
          slot: idx,
          x: r.left,
          y: r.top,
          w: r.width,
          h: r.height,
          filled: filled,
          name: filled ? String(app) : "",
          url: filled ? pageUrl : ""
        });
      }
      try {
        host.postMessage({
          type: "adc-slot-bounds",
          space: stageTab,
          vw: window.innerWidth,
          vh: window.innerHeight,
          count: state.slots.length,
          slots: slots
        });
      } catch(boundsErr){}
    }
    window.__adcReportSlots = reportSlotBounds;
    /* 2026-10-11 adc-v157: the WV2 host shows one line in a desktop-app pane, e.g. when a running app gave no window. */
    window.__adcEmbedNote = function(idx, space, text){
      if(space && space !== stageTab) return;
      var wait = grid && grid.querySelector('.lp-slot[data-slot="' + idx + '"] .adc-embed-wait');
      if(!wait) return;
      wait.textContent = String(text || '');
      wait.classList.add('adc-embed-note');
    };
    /* The WV2 host calls this when an embedded desktop app exits. That pane is cleared. */
    window.__adcEmbedEnded = function(idx, space){
      if(space && space !== stageTab){
        // a program in a tab that is not on screen ended: clear that tab's slot quietly
        var st = stages[space];
        if(st && Array.isArray(st.slots) && st.loads && st.loads[idx] && desktopTarget(st.loads[idx].url)){
          st.slots[idx] = null; st.loads[idx] = null; persistStages();
        }
        return;
      }
      if(!state || !Array.isArray(state.slots) || idx < 0 || idx >= state.slots.length) return;
      var load = state.loads && state.loads[idx];
      if(!load || !desktopTarget(load.url)) return;
      state.slots[idx] = null;
      state.loads[idx] = null;
      render();
      writePageOverlay();
    };
    /* Puts an added L-stack pane on the right, spanning the left pane the way that layout already does. */
    function placeStackSlots(){
      if(!/^l[234]$/.test(state.layout)) return;
      var nodes = grid.querySelectorAll('.lp-slot');
      var rows = Math.max(1, nodes.length - 1);
      if(nodes[0]) nodes[0].style.gridRow = '1 / span ' + rows;
      for(var n = 1; n < nodes.length; n++){
        nodes[n].style.gridColumn = '2';
        nodes[n].style.gridRow = String(n);
      }
    }
    /* Draws the active tab's slots for its current layout. */
    function render(){
      if(!state.slots.length) state.slots.push(null);
      ensureLoads();
      var nat = natives[stageTab], holder = holders[stageTab];
      if(!nat) state.slots = state.slots.map(function(a){ return a === NATIVE ? null : a; });
      // Park the native view before clearing the grid so its DOM (and listeners) survive.
      if(nat && holder && nat.parentNode !== holder) holder.appendChild(nat);
      var nativePlaced = state.slots.indexOf(NATIVE) >= 0;
      var meta = LAYOUTS[state.layout] || LAYOUTS.half || { cls: '' };
      grid.className = 'lp-stage-grid ' + meta.cls;
      var dying = grid.querySelectorAll('iframe');
      for(var f = 0; f < dying.length; f++) discardFrame(dying[f]);
      grid.innerHTML = '';
      for(var i=0;i<state.slots.length;i++){
        (function(idx){
          var app = state.slots[idx];
          var slot = document.createElement('div');
          slot.className = 'lp-slot ' + (app ? 'filled' : 'empty');
          slot.dataset.slot = String(idx);
          if(state.activeSlot === idx && !app) slot.classList.add('active-target');
          if(!app){
            slot.innerHTML = '<div class="drop-hint"><span>Drop a card here to load its page</span><button type="button" class="lp-delete-pane" data-delete-pane="'+idx+'">Delete</button></div>';
            if(nat && !nativePlaced){
              slot.insertAdjacentHTML('beforeend', '<button type="button" class="lp-restore-native" data-restore-native="'+idx+'">Restore '+esc(TAB_LABELS[stageTab]||stageTab)+' view</button>');
            }
          } else if(app === NATIVE){
            slot.classList.add('native-slot');
            slot.innerHTML =
              '<div class="lp-pane-h" draggable="true" data-slot-drag="'+idx+'">' +
                '<span class="grip"></span>' +
                '<span class="app-name">▣ '+esc(TAB_LABELS[stageTab]||stageTab)+' · tab view</span>' +
                '<button type="button" data-close-slot="'+idx+'" title="Hide this tab view (restore from any empty slot)">Close</button>' +
              '</div>' +
              '<div class="lp-pane-b lp-native-b"></div>';
            slot.querySelector('.lp-native-b').appendChild(nat);
          } else {
            var paneUrl = resolvePane(idx, app).url || '';
            slot.innerHTML =
              '<div class="lp-pane-h" draggable="true" data-slot-drag="'+idx+'">' +
                '<span class="grip"></span>' +
                '<span class="app-name">'+(ICONS[app]||'')+' '+esc(app)+'</span>' +
                (paneUrl ? '<span class="app-url" title="'+esc(paneUrl)+'">'+esc(paneUrl)+'</span>' : '') +
                '<button type="button" data-detach-slot="'+idx+'">Detach</button>' +
                '<button type="button" data-close-slot="'+idx+'">Close</button>' +
              '</div>' +
              '<div class="lp-pane-b" data-pane-body="1"></div>';
            var paneBody = slot.querySelector('[data-pane-body]');
            if(paneBody) fillPaneBody(paneBody, app, idx);
          }
          slot.addEventListener('click', function(e){
            if(e.target.closest('button')) return;
            if(e.target.closest('.lp-pane-h')) return;
            state.activeSlot = idx;
            render();
          });
          slot.addEventListener('dragover', function(e){
            e.preventDefault();
            slot.classList.add('drag-over');
          });
          slot.addEventListener('dragleave', function(){
            slot.classList.remove('drag-over');
          });
          slot.addEventListener('drop', function(e){
            e.preventDefault();
            e.stopPropagation();
            slot.classList.remove('drag-over');
            handleDrop(idx);
          });
          grid.appendChild(slot);
        })(i);
      }
      // pane title drag + buttons
      grid.querySelectorAll('[data-slot-drag]').forEach(function(hdr){
        /* 2026-10-09 (adc-panedrag) WV2: pane headers move with a pointer drag, not HTML5 drag-and-drop.
           In the real WebView2 the header's HTML5 drag was cancelled at once (dragstart, then dragend
           with dropEffect none and no dragenter; trace 10:21 PM), so panes never moved. Pointer capture does
           not use the OS drag loop. Drop on another pane swaps them (same handleDrop as before); Esc cancels. */
        if(window.chrome && window.chrome.webview){
          hdr.setAttribute('draggable', 'false');
          hdr.addEventListener('dragstart', function(e){ e.preventDefault(); });
          hdr.addEventListener('pointerdown', function(e){
            if(e.button !== 0 || (e.target.closest && e.target.closest('button,input,a,select,textarea'))) return;
            var from = parseInt(hdr.getAttribute('data-slot-drag'), 10);
            var sx = e.clientX, sy = e.clientY, started = false, over = null, pid = e.pointerId;
            try { hdr.setPointerCapture(pid); } catch(_){}
            function target(ev){
              var el = document.elementFromPoint(ev.clientX, ev.clientY);
              var s = el && el.closest ? el.closest('.lp-slot') : null;
              return (s && grid.contains(s)) ? s : null;
            }
            var done = false, moved = false, DT = window.__adcDT || function(){};
            function lost(){ DT('pxlost'); }
            function move(ev){
              if(done) return;
              if(!moved){ moved = true; DT('pxmove'); }
              if(ev.buttons === 0 && ev.type === 'mousemove'){ finish(ev, false); return; }
              if(!started){
                if(Math.abs(ev.clientX - sx) + Math.abs(ev.clientY - sy) < 6) return;
                started = true;
                state.drag = { source: 'slot', from: from, app: state.slots[from] };
                hdr.style.opacity = '.5';
                document.body.classList.add('adc-dragging');
                document.body.classList.add('adc-pane-dragging');
                DT('pxstart');
              }
              var s = target(ev);
              if(s !== over){
                if(over) over.classList.remove('drag-over');
                over = s;
                if(over && parseInt(over.dataset.slot, 10) !== from) over.classList.add('drag-over');
              }
            }
            function finish(ev, cancel){
              if(done) return; done = true;
              window.removeEventListener('pointermove', move, true);
              window.removeEventListener('mousemove', move, true);
              window.removeEventListener('pointerup', up, true);
              window.removeEventListener('mouseup', up, true);
              window.removeEventListener('pointercancel', cxl, true);
              hdr.removeEventListener('lostpointercapture', lost);
              document.removeEventListener('keydown', escKey, true);
              try { hdr.releasePointerCapture(pid); } catch(_){}
              if(!started){ DT('pxclick'); return; }
              hdr.style.opacity = '';
              grid.querySelectorAll('.drag-over').forEach(function(el){ el.classList.remove('drag-over'); });
              var s = cancel ? null : target(ev);
              var to = s ? parseInt(s.dataset.slot, 10) : -1;
              DT(cancel ? 'pxcancel' : (s && isFinite(to) && to !== from ? 'pxdrop' : 'pxnone'));
              if(s && isFinite(to) && to !== from) handleDrop(to);
              state.drag = null;
              document.body.classList.remove('adc-dragging');
              document.body.classList.remove('adc-pane-dragging');
            }
            function up(ev){ finish(ev, false); }
            function cxl(ev){ finish(ev, true); }
            function escKey(ev){ if(ev.key === 'Escape'){ ev.preventDefault(); ev.stopPropagation(); finish(ev, true); } }
            /* 2026-10-09 10:40 PM (adc-panedrag2): moves and the release are read at window level (pointer AND mouse
               events), a lost capture does not end the drag, and a move with no button down ends it as a drop. */
            DT('pxdown');
            window.addEventListener('pointermove', move, true);
            window.addEventListener('mousemove', move, true);
            window.addEventListener('pointerup', up, true);
            window.addEventListener('mouseup', up, true);
            window.addEventListener('pointercancel', cxl, true);
            hdr.addEventListener('lostpointercapture', lost);
            document.addEventListener('keydown', escKey, true);
          });
          return;
        }
        hdr.addEventListener('dragstart', function(e){
          var from = parseInt(hdr.getAttribute('data-slot-drag'), 10);
          state.drag = { source: 'slot', from: from, app: state.slots[from] };
          try {
            e.dataTransfer.setData('text/plain', state.slots[from] || '');
            e.dataTransfer.effectAllowed = 'move';
          } catch(_){}
          hdr.style.opacity = '.5';
          document.body.classList.add('adc-dragging');
        });
        hdr.addEventListener('dragend', function(){
          hdr.style.opacity = '';
          document.body.classList.remove('adc-dragging');
          state.drag = null;
          grid.querySelectorAll('.drag-over').forEach(function(el){ el.classList.remove('drag-over'); });
        });
      });
      grid.querySelectorAll('[data-close-slot]').forEach(function(btn){
        btn.addEventListener('click', function(e){
          e.stopPropagation();
          var i = parseInt(btn.getAttribute('data-close-slot'), 10);
          state.slots[i] = null;
          if(state.loads) state.loads[i] = null;
          if(typeof wv2Posted !== "undefined") delete wv2Posted[i];
          state.activeSlot = i;
          render();
        });
      });
      grid.querySelectorAll('[data-delete-pane]').forEach(function(btn){
        btn.addEventListener('click', function(e){
          e.stopPropagation();
          var i = parseInt(btn.getAttribute('data-delete-pane'), 10);
          if(state.slots[i]) return;
          if(state.slots.length <= 1){
            toast('Keep at least one pane');
            return;
          }
          state.slots.splice(i, 1);
          if(Array.isArray(state.loads)) state.loads.splice(i, 1);
          if(typeof wv2Posted !== "undefined"){
            Object.keys(wv2Posted).forEach(function(k){
              var n = parseInt(k, 10);
              if(n >= i) delete wv2Posted[n];
            });
          }
          if(state.activeSlot === i) state.activeSlot = null;
          else if(typeof state.activeSlot === 'number' && state.activeSlot > i) state.activeSlot -= 1;
          render();
        });
      });
      grid.querySelectorAll('[data-restore-native]').forEach(function(btn){
        btn.addEventListener('click', function(e){
          e.stopPropagation();
          var i = parseInt(btn.getAttribute('data-restore-native'), 10);
          state.slots[i] = NATIVE;
          state.activeSlot = null;
          render();
        });
      });
      grid.querySelectorAll('[data-detach-slot]').forEach(function(btn){
        btn.addEventListener('click', function(e){
          e.stopPropagation();
          var i = parseInt(btn.getAttribute('data-detach-slot'), 10);
          var app = state.slots[i];
          if(!app) return;
          var info = resolvePane(i, app);
          var url = info && info.url ? String(info.url) : '';
          if(!url){
            toast(app + ' has no page yet');
            return;
          }
          var opened = openDetachedPane(url, info.cardId || app);
          if(!opened){
            toast('Detach did not open a window - WV2 keeps card pages in their stage panes');
            return;
          }
          var nameEl = btn.parentElement.querySelector('.app-name');
          if(nameEl){
            nameEl.classList.add('detached-flash');
            nameEl.textContent = (ICONS[app]||'') + ' ' + app + ' \u00b7 detached';
            setTimeout(function(){
              if(nameEl) { nameEl.classList.remove('detached-flash'); nameEl.textContent = (ICONS[app]||'') + ' ' + app; }
            }, 1400);
          }
          var ghost = document.createElement('div');
          ghost.className = 'lp-float-ghost lp-slot filled';
          ghost.innerHTML = '<div class="lp-pane-h"><span class="grip"></span><span class="app-name">'+(ICONS[app]||'')+' '+esc(app)+' \u00b7 detached</span></div><div class="lp-pane-b"><div class="sample-block">Detached stub</div></div>';
          ghost.style.left = (window.innerWidth - 360) + 'px';
          ghost.style.top = '80px';
          document.body.appendChild(ghost);
          setTimeout(function(){ if(ghost.parentNode) ghost.parentNode.removeChild(ghost); }, 1600);
          toast('Detached \u00b7 ' + app);
        });
      });

      placeStackSlots();
      refreshStageGutters();
      persistStages();
      scheduleSlotReport();
    }

    /* Places a dragged app into a stage slot. */
    function handleDrop(toIdx){
      // toIdx is the slot that received the drop. The left stage slot is 0.
      var d = state.drag;
      if(window.__adcTabDrag) return;
      if(window.__adcGroupDrag) return;
      if(!d || !d.app){
        toast('Drop a tray card on a pane. Nothing was loaded.');
        return;
      }
      ensureLoads();
      var moved = '';
      if(d.source === 'tray'){
        // replace target (occupied or not); a replaced tab view just returns to its holder
        state.slots[toIdx] = d.app;
        state.loads[toIdx] = { cardId: d.cardId || '', url: d.url || '' };
      } else if(d.source === 'slot'){
        var from = d.from;
        if(from === toIdx) return;
        var tmp = state.slots[toIdx];
        state.slots[toIdx] = state.slots[from];
        state.slots[from] = tmp;
        var tmpLoad = state.loads[toIdx] || null;
        state.loads[toIdx] = state.loads[from] || null;
        state.loads[from] = tmpLoad;
        moved = d.app;
        if(typeof movePaneOnHost === 'function') movePaneOnHost(from, toIdx);
      } else {
        toast('Drop a tray card on a pane. Nothing was loaded.');
        return;
      }
      state.activeSlot = null;
      state.drag = null;
      document.body.classList.remove('adc-dragging');
      var droppedApp = d.app;
      var droppedFromTray = d.source === 'tray';
      render();
      writePageOverlay();
      if(droppedFromTray){
        var got = state.loads && state.loads[toIdx];
        toast(got && got.url ? ('Loaded ' + droppedApp) : (droppedApp + ' has no page yet'));
        if(got && desktopTarget(got.url)) toast('Starting ' + droppedApp + ' in this pane');
      } else if(moved) toast('Moved ' + moved);
    }

    /* Switches the active tab's layout and keeps filled slots. */
    function setLayout(key){
      var meta = LAYOUTS[key];
      if(!meta) return;
      ensureLoads();
      var prev = [];
      var prevLoads = [];
      state.slots.forEach(function(a, i){
        if(a){ prev.push(a); prevLoads.push(state.loads[i] || null); }
      });
      state.layout = key;
      state.slots = [];
      state.loads = [];
      for(var i=0;i<meta.count;i++){
        state.slots.push(prev[i] || null);
        state.loads.push(prev[i] ? (prevLoads[i] || null) : null);
      }
      // extras dropped (back to tray conceptually)
      state.activeSlot = null;
      persistStages();
      syncLayoutButtons();
      render();
    }
    /* Highlights the active layout and updates the toolbar tab name. */
    function syncLayoutButtons(){
      document.querySelectorAll('#lpLayoutBtns .lp-layout-btn').forEach(function(b){
        b.classList.toggle('active', b.dataset.layout === state.layout);
      });
      var lbl = document.getElementById('lpToolbarTab');
      if(lbl) lbl.textContent = TAB_LABELS[stageTab] || stageTab;
    }
    /* Points stage editing at one tab's grid. */
    function switchStage(tab){
      if(!tab) return;
      if(!grids[tab]){
        var tabsModel = window.__ADC && window.__ADC.model && window.__ADC.model.tabs;
        var found = null;
        if(tabsModel){
          for(var ti=0; ti<tabsModel.length; ti++){
            if(tabsModel[ti] && tabsModel[ti].id === tab) found = tabsModel[ti];
          }
        }
        if(TAB_IDS.indexOf(tab) < 0) TAB_IDS.push(tab);
        if(!TAB_LABELS[tab]) TAB_LABELS[tab] = found ? (found.label || tab) : tab;
        ensureTabStage(tab);
        if(!stages[tab]) stages[tab] = normStage(tab, null);
      }
      if(!grids[tab] || !stages[tab]) return;
      var view = document.getElementById('view-' + tab);
      if(view){
        document.querySelectorAll('.view').forEach(function(v){ v.classList.remove('active'); });
        view.classList.add('active');
      }
      stageTab = tab;
      state = stages[tab];
      grid = grids[tab];
      syncLayoutButtons();
      render();
    }
    document.addEventListener('cockpit:tab', function(e){ switchStage(e.detail); });

    /* Binds layout buttons once so a click changes the layout. */
    function bindLayoutButtons(){
    document.querySelectorAll('#lpLayoutBtns .lp-layout-btn').forEach(function(btn){
        if(btn.dataset.adcBound) return;
        btn.dataset.adcBound = '1';
      btn.addEventListener('click', function(){ setLayout(btn.dataset.layout); });
    });
    }
    bindLayoutButtons();
    /* Adds one empty pane on whatever layout is selected, including the presets. */
    function addPane(){
      if(state.slots.length >= 16){
        toast('16 panes is the limit');
        return;
      }
      ensureLoads();
      state.slots.push(null);
      state.loads.push(null);
      state.activeSlot = state.slots.length - 1;
      render();
    }
    var addPaneBtn = document.getElementById('lpAddPane');
    if(addPaneBtn && !addPaneBtn.dataset.adcBound){
      addPaneBtn.dataset.adcBound = '1';
      addPaneBtn.addEventListener('click', function(){ addPane(); });
    }


    /* Shows the Launch Pad view when it is hidden. */
    function ensureLaunchView(){
      var selected = document.querySelector('.tab.active');
      var selectedId = selected && selected.getAttribute('data-view');
      if(selectedId && selectedId !== 'launch') return;
      var launch = document.getElementById('view-launch');
      if(launch && !launch.classList.contains('active')){
        if(typeof window.__activateView === 'function') window.__activateView('launch');
        else {
          document.querySelectorAll('.tab').forEach(function(t){t.classList.toggle('active', t.dataset.view==='launch')});
          document.querySelectorAll('.view').forEach(function(v){v.classList.remove('active')});
          launch.classList.add('active');
        }
      }
    }
    /* A card whose url is a local .exe (C:\...\bambu-studio.exe) or a desktop app link (bambustudio://open)
       is started by the WV2 host in its own window. It does not fill a pane: a pane is a web view. */
    /* The url when it is a local .exe or a desktop app link, else ''. Web, file pages, and scripts return ''. */
    function desktopTarget(raw){
      var url = String(raw || '').replace(/^\s+|\s+$/g, '');
      var exe = /^([a-z]:[\\\/]|\\\\|file:)/i.test(url) && /\.exe"?$/i.test(url);
      var link = /^[a-z][a-z0-9+.-]+:/i.test(url) && !/^(https?|file|about|data|javascript|blob):/i.test(url);
      return (exe || link) ? url : '';
    }
    /* Asks the WV2 host to start a desktop program in its own window. */
    function launchDesktopUrl(name, url){
      var host = window.chrome && window.chrome.webview;
      if(!url || !host || typeof host.postMessage !== 'function') return false;
      try { host.postMessage({ type: 'adc-launch', name: String(name || ''), url: url }); } catch(_){ return false; }
      toast('Opening ' + name + ' in its own window');
      return true;
    }
    function launchDesktopCard(app, cardId){
      var card = cardById(cardId) || cardByApp(app);
      var url = desktopTarget(card && card.url);
      if(!url) return false;
      return launchDesktopUrl((card && card.name) || app, url);
    }
    /* Puts a tray app into the active slot, or the first open slot. */
    function placeAppFromTray(app, cardId){
      if(launchDesktopCard(app, cardId)) return;
      // Fills the ACTIVE tab's stage (no longer forces the Launch Pad tab).
      if(trayCollapsed) setTrayCollapsed(false, true);
      var target = state.activeSlot;
      if(typeof target === "number" && target >= state.slots.length) target = null;
      if(target == null){
        target = -1;
        for(var i=0;i<state.slots.length;i++){
          if(!state.slots[i]){ target = i; break; }
        }
        // Mockup: if stage is full, replace first slot so tray clicks always land.
        if(target < 0) target = 0;
      }
      var placed = cardById(cardId) || cardByApp(app);
      ensureLoads();
      state.slots[target] = app;
      state.loads[target] = { cardId: placed && placed.id || '', url: placed && placed.url ? String(placed.url) : '' };
      state.activeSlot = null;
      render();
      writePageOverlay();
    }
    /* Binds drag and click on tray tiles that are not bound yet. */
    function bindTrayTiles(){
    document.querySelectorAll('.lp-tray .lp-tile[data-app]').forEach(function(tile){
        if(tile.dataset.adcBound) return;
        tile.dataset.adcBound = '1';
      tile.addEventListener('dragstart', function(e){
        var app = tile.dataset.app;
        state.drag = { source: 'tray', app: app, cardId: tile.getAttribute('data-card-id') || '', url: tile.getAttribute('data-url') || '' };
        window.__adcTrayDrag = { cardId: state.drag.cardId, app: app, url: state.drag.url };
        window.__adcSuppressCardClick = true;
        tile.dataset.adcSuppressClick = '1';
        document.body.classList.add('adc-dragging');
        tile.classList.add('dragging');
        try {
          e.dataTransfer.setData('text/plain', app);
          e.dataTransfer.effectAllowed = 'copyMove';
        } catch(_){}
      });
      tile.addEventListener('dragend', function(){
        document.body.classList.remove('adc-dragging');
        tile.classList.remove('dragging');
        state.drag = null;
        grid.querySelectorAll('.drag-over').forEach(function(el){ el.classList.remove('drag-over'); });
        document.querySelectorAll('.adc-group-drop').forEach(function(el){ el.classList.remove('adc-group-drop'); });
        var ended = window.__adcTrayDrag;
        requestAnimationFrame(function(){
          if(window.__adcTrayDrag === ended) window.__adcTrayDrag = null;
        });
        setTimeout(function(){ window.__adcSuppressCardClick = false; }, 400);
      });
      tile.addEventListener('click', function(e){
        if(tile.dataset.adcSuppressClick === '1' || window.__adcSuppressCardClick){
          tile.dataset.adcSuppressClick = '';
          window.__adcSuppressCardClick = false;
          e.preventDefault();
          e.stopPropagation();
          return;
        }
        placeAppFromTray(tile.dataset.app, tile.getAttribute('data-card-id') || '');
      });
    });
    }
    bindTrayTiles();

    /* Binds collapsed-rail clicks that place an app on the stage. */
    function bindRail(){
    document.querySelectorAll('.lp-tray-rail .rail-ico').forEach(function(btn){
        if(btn.dataset.adcBound) return;
        btn.dataset.adcBound = '1';
      btn.addEventListener('click', function(){
        var app = btn.getAttribute('data-rail-app');
        if(app) placeAppFromTray(app);
        else setTrayCollapsed(false, true);
      });
    });
    }
    bindRail();


    /* ===== Configs + Config Groups (localStorage) ===== */
    var CONFIGS_KEY = 'lp-configs';
    var GROUPS_KEY = 'lp-config-groups';
    var modalEl = document.getElementById('lpModal');
    var modalTitle = document.getElementById('lpModalTitle');
    var modalHint = document.getElementById('lpModalHint');
    var modalBody = document.getElementById('lpModalBody');
    var modalActions = document.getElementById('lpModalActions');
    var configsDd = document.getElementById('lpConfigsDd');
    var groupsDd = document.getElementById('lpGroupsDd');
    var configsMenu = document.getElementById('lpConfigsMenu');
    var groupsMenu = document.getElementById('lpGroupsMenu');

    /* Makes an id for a saved config or group. */
    function uid(prefix){
      return (prefix || 'id') + '_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8);
    }
    /* Reads a JSON value from localStorage. */
    function readJson(key, fallback){
      try {
        var raw = localStorage.getItem(key);
        if(raw == null) return fallback;
        var v = JSON.parse(raw);
        return v == null ? fallback : v;
      } catch(_){ return fallback; }
    }
    /* Writes a JSON value to localStorage. */
    function writeJson(key, val){
      try { localStorage.setItem(key, JSON.stringify(val)); return true; }
      catch(_){ toast('localStorage write failed'); return false; }
    }
    /* Returns saved screen configs. */
    function loadConfigs(){
      var list = readJson(CONFIGS_KEY, []);
      return Array.isArray(list) ? list : [];
    }
    /* Stores screen configs. */
    function saveConfigs(list){ return writeJson(CONFIGS_KEY, list); }
    /* Returns saved config groups. */
    function loadGroups(){
      var list = readJson(GROUPS_KEY, []);
      return Array.isArray(list) ? list : [];
    }
    /* Stores config groups. */
    function saveGroups(list){ return writeJson(GROUPS_KEY, list); }
    /* Finds a saved config by id. */
    function findConfig(id){
      var list = loadConfigs();
      for(var i=0;i<list.length;i++){ if(list[i] && list[i].id === id) return list[i]; }
      return null;
    }
    /* Finds a saved config group by id. */
    function findGroup(id){
      var list = loadGroups();
      for(var i=0;i<list.length;i++){ if(list[i] && list[i].id === id) return list[i]; }
      return null;
    }
    /** Ensure every config has a matching group-of-one (same name) for Open group. */
    function syncSoloGroup(cfg){
      if(!cfg || !cfg.id) return;
      var groups = loadGroups();
      var idx = -1;
      for(var i=0;i<groups.length;i++){
        var g = groups[i];
        var ids = g.configIds || [];
        if(ids.length === 1 && ids[0] === cfg.id){ idx = i; break; }
      }
      if(idx < 0){
        for(var j=0;j<groups.length;j++){
          var g2 = groups[j];
          var ids2 = g2.configIds || [];
          if(g2.name === cfg.name && ids2.length <= 1){ idx = j; break; }
        }
      }
      if(idx >= 0){
        groups[idx].name = cfg.name;
        groups[idx].configIds = [cfg.id];
      } else {
        groups.push({ id: uid('grp'), name: cfg.name, configIds: [cfg.id] });
      }
      saveGroups(groups);
    }
    /* Reads the tray width in pixels. */
    function currentTrayWidth(){
      if(trayCollapsed){
        try {
          var saved = localStorage.getItem('lp-tray-width');
          if(saved != null && isFinite(Number(saved))) return Math.round(Number(saved));
        } catch(_){}
        return DEFAULT_TRAY_WIDTH;
      }
      if(lpShell){
        var cs = getComputedStyle(lpShell).getPropertyValue('--lp-tray-width');
        var n = parseFloat(cs);
        if(isFinite(n) && n > 0) return Math.round(n);
      }
      return DEFAULT_TRAY_WIDTH;
    }
    /* Returns the active tab id. */
    function currentTab(){
      var t = document.querySelector('.tab.active');
      return (t && t.dataset.view) ? t.dataset.view : 'launch';
    }
    /* Snapshots the active tab's layout, slots, sizes, and tray. */
    function captureConfig(name, existingId){
      return {
        id: existingId || uid('cfg'),
        name: name,
        savedAt: new Date().toISOString(),
        layout: state.layout,
        slots: state.slots.slice(),
        sizes: cloneSizes(getSizesFor(state.layout)),
        trayWidth: currentTrayWidth(),
        trayCollapsed: !!trayCollapsed,
        tab: stageTab,
        stageVersion: 2
      };
    }
    /* Restores a saved config onto its stage. */
    function applyConfig(cfg){
      if(!cfg) return false;
      var layout = (cfg.layout && LAYOUTS[cfg.layout]) ? cfg.layout : 'half';
      var meta = LAYOUTS[layout];
      var slots = Array.isArray(cfg.slots) ? cfg.slots.slice() : [];
      while(slots.length < meta.count) slots.push(null);
      // v2 configs restore the stage of the tab they were saved on. Legacy configs (pre per-tab)
      // always described the Launch Pad stage, so restore that stage and then show cfg.tab as before.
      var targetTab = (cfg.tab && grids[cfg.tab]) ? cfg.tab : 'launch';
      var stageFor = cfg.stageVersion === 2 ? targetTab : 'launch';
      var st = stages[stageFor];
      if(!natives[stageFor]) slots = slots.map(function(a){ return a === NATIVE ? null : a; });
      st.layout = layout;
      st.slots = slots.slice();
      st.loads = st.slots.map(function(){ return null; });
      st.activeSlot = null;
      if(cfg.sizes && Array.isArray(cfg.sizes.cols) && cfg.sizes.cols.length){
        if(!st.sizes || typeof st.sizes !== 'object') st.sizes = {};
        st.sizes[layout] = cloneSizes(cfg.sizes);
      }
      persistStages();
      if(typeof cfg.trayWidth === 'number' || (cfg.trayWidth != null && isFinite(Number(cfg.trayWidth)))){
        var tw = Math.round(Number(cfg.trayWidth));
        try { localStorage.setItem('lp-tray-width', String(tw)); } catch(_){}
        if(!trayCollapsed) setTrayWidth(tw, true);
      }
      if(typeof cfg.trayCollapsed === 'boolean'){
        setTrayCollapsed(cfg.trayCollapsed, true);
      }
      var showTab = cfg.stageVersion === 2 ? targetTab : (cfg.tab || 'launch');
      if(typeof window.__activateView === 'function') window.__activateView(showTab);
      switchStage(currentTab());
      return true;
    }
    /* Builds a page URL that reopens one config. */
    function configUrl(id){
      var base = location.href.split('#')[0];
      var qIdx = base.indexOf('?');
      if(qIdx >= 0) base = base.slice(0, qIdx);
      return base + '?config=' + encodeURIComponent(id);
    }
    /* Reads a config id from the page query string. */
    function configIdFromUrl(){
      try {
        var q = new URLSearchParams(location.search || '');
        return q.get('config');
      } catch(_){
        var m = /(?:\?|&)config=([^&]+)/.exec(location.search || '');
        return m ? decodeURIComponent(m[1]) : null;
      }
    }
    /* Closes the config and group menus. */
    function closeAllDropdowns(){
      [configsDd, groupsDd].forEach(function(dd){
        if(!dd) return;
        dd.classList.remove('open');
        var btn = dd.querySelector('button');
        if(btn) btn.setAttribute('aria-expanded', 'false');
      });
    }
    /* Opens one menu and closes the others. */
    function toggleDropdown(dd){
      if(!dd) return;
      var willOpen = !dd.classList.contains('open');
      closeAllDropdowns();
      if(willOpen){
        dd.classList.add('open');
        var btn = dd.querySelector('button');
        if(btn) btn.setAttribute('aria-expanded', 'true');
      }
    }
    /* Hides the shared dialog and clears it. */
    function closeModal(){
      if(!modalEl) return;
      modalEl.classList.remove('show');
      modalEl.setAttribute('aria-hidden', 'true');
      if(modalBody) modalBody.innerHTML = '';
      if(modalActions) modalActions.innerHTML = '';
      if(modalHint){ modalHint.hidden = true; modalHint.textContent = ''; }
    }
    /* Shows the shared dialog with a body and actions. */
    function openModal(opts){
      if(!modalEl) return;
      var dialog = modalEl.querySelector('.lp-modal');
      if(dialog && !dialog.querySelector('.lp-modal-x')){
        var x = document.createElement('button');
        x.type = 'button';
        x.className = 'lp-modal-x';
        x.setAttribute('aria-label', 'Close');
        x.title = 'Close';
        x.textContent = '\u00d7';
        x.addEventListener('click', function(ev){
          ev.preventDefault();
          ev.stopPropagation();
          closeModal();
        });
        dialog.insertBefore(x, dialog.firstChild);
      }
      closeAllDropdowns();
      modalTitle.textContent = opts.title || 'Dialog';
      if(opts.hint){
        modalHint.hidden = false;
        modalHint.textContent = opts.hint;
      } else {
        modalHint.hidden = true;
        modalHint.textContent = '';
      }
      modalBody.innerHTML = '';
      if(typeof opts.buildBody === 'function') opts.buildBody(modalBody);
      modalActions.innerHTML = '';
      (opts.actions || []).forEach(function(a){
        var b = document.createElement('button');
        b.type = 'button';
        b.textContent = a.label;
        if(a.primary) b.className = 'primary';
        b.addEventListener('click', function(){
          if(a.onClick){
            var keep = a.onClick();
            if(keep === true) return;
          }
          closeModal();
        });
        modalActions.appendChild(b);
      });
      modalEl.classList.add('show');
      modalEl.setAttribute('aria-hidden', 'false');
      var focusEl = modalBody.querySelector('input,button,a');
      if(focusEl) setTimeout(function(){ try { focusEl.focus(); focusEl.select && focusEl.select(); } catch(_){} }, 30);
    }
    if(modalEl){
      modalEl.addEventListener('click', function(e){
        if(e.target === modalEl) closeModal();
      });
    }
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape'){
        if(modalEl && modalEl.classList.contains('show')) closeModal();
        else closeAllDropdowns();
      }
    });
    document.addEventListener('click', function(e){
      if(e.target.closest && e.target.closest('.lp-dd')) return;
      closeAllDropdowns();
    });

    /* Formats a saved timestamp for the menus. */
    function fmtSavedAt(iso){
      if(!iso) return '';
      try {
        var d = new Date(iso);
        if(isNaN(d.getTime())) return iso;
        return d.toLocaleString();
      } catch(_){ return iso; }
    }
    /* Fills the configs menu. */
    function renderConfigsMenu(){
      if(!configsMenu) return;
      var list = loadConfigs().slice().sort(function(a,b){
        return String(b.savedAt||'').localeCompare(String(a.savedAt||''));
      });
      configsMenu.innerHTML = '';
      if(!list.length){
        configsMenu.innerHTML = '<div class="lp-dd-empty">No saved configs yet<br><span class="dd-hint">Save config also creates an Open group of one.</span></div>';
        return;
      }
      list.forEach(function(cfg){
        var row = document.createElement('div');
        row.className = 'lp-dd-item';
        var meta = document.createElement('div');
        meta.className = 'meta';
        meta.innerHTML = '<div class="name"></div><div class="sub"></div>';
                var nameEl = meta.querySelector('.name');
        nameEl.textContent = cfg.name || cfg.id;
        nameEl.classList.add('lp-cfg-load');
        nameEl.setAttribute('role', 'button');
        nameEl.tabIndex = 0;
        nameEl.title = 'Load config';
        nameEl.addEventListener('click', function(e){
          e.stopPropagation();
          closeAllDropdowns();
          if(applyConfig(cfg)) toast('Loaded “' + (cfg.name || cfg.id) + '”');
        });
        nameEl.addEventListener('keydown', function(e){
          if(e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') return;
          e.preventDefault();
          e.stopPropagation();
          closeAllDropdowns();
          if(applyConfig(cfg)) toast('Loaded “' + (cfg.name || cfg.id) + '”');
        });
        var cfgTabLabel = TAB_LABELS[cfg.tab] || cfg.tab || 'Launch Pad';
        var slotNames = (cfg.slots || []).filter(Boolean).map(function(a){ return a === NATIVE ? (cfgTabLabel + ' view') : a; }).join(', ') || 'empty';
        meta.querySelector('.sub').textContent = cfgTabLabel + ' · ' + (cfg.layout || '?') + ' · ' + slotNames + ' · ' + fmtSavedAt(cfg.savedAt);
        var acts = document.createElement('div');
        acts.className = 'acts';
        var loadBtn = document.createElement('button');
        loadBtn.type = 'button';
        loadBtn.textContent = 'Load';
        loadBtn.title = 'Apply in this window (edit while building)';
        loadBtn.addEventListener('click', function(e){
          e.stopPropagation();
          closeAllDropdowns();
          if(applyConfig(cfg)) toast('Loaded “' + (cfg.name || cfg.id) + '”');
        });
        var renBtn = document.createElement('button');
        renBtn.type = 'button';
        renBtn.textContent = 'Rename';
        renBtn.addEventListener('click', function(e){
          e.stopPropagation();
          closeAllDropdowns();
          promptRenameConfig(cfg.id, cfg.name);
        });
        var delBtn = document.createElement('button');
        delBtn.type = 'button';
        delBtn.className = 'danger';
        delBtn.textContent = 'Delete';
        delBtn.addEventListener('click', function(e){
          e.stopPropagation();
          var next = loadConfigs().filter(function(c){ return c.id !== cfg.id; });
          saveConfigs(next);
          // prune from groups
          var groups = loadGroups().map(function(g){
            return {
              id: g.id,
              name: g.name,
              configIds: (g.configIds || []).filter(function(id){ return id !== cfg.id; })
            };
          });
          saveGroups(groups);
          renderConfigsMenu();
          renderGroupsMenu();
          toast('Deleted config');
        });
        var owBtn = document.createElement('button');
        owBtn.type = 'button';
        owBtn.textContent = 'Overwrite';
        owBtn.title = 'Snapshot the current stage over this config';
        owBtn.addEventListener('click', function(e){
          e.stopPropagation();
          var owList = loadConfigs();
          var owIdx = -1;
          for(var oi=0;oi<owList.length;oi++){
            if(owList[oi].id === cfg.id){ owIdx = oi; break; }
          }
          if(owIdx < 0){ toast('Config not found'); return; }
          var snap = captureConfig(cfg.name, cfg.id);
          owList[owIdx] = snap;
          saveConfigs(owList);
          syncSoloGroup(snap);
          renderConfigsMenu();
          renderGroupsMenu();
          toast('Overwrote “' + (cfg.name || cfg.id) + '”');
        });
        acts.appendChild(loadBtn);
        acts.appendChild(renBtn);
        acts.appendChild(owBtn);
        acts.appendChild(delBtn);
        row.appendChild(meta);
        row.appendChild(acts);
        configsMenu.appendChild(row);
      });
    }
    /* Fills the Open group menu. */
    function renderGroupsMenu(){
      if(!groupsMenu) return;
      var list = loadGroups().slice();
      var cfgs = loadConfigs();
      var byId = {};
      cfgs.forEach(function(c){ byId[c.id] = c; });
      groupsMenu.innerHTML = '';
      if(!list.length){
        groupsMenu.innerHTML = '<div class="lp-dd-empty">No groups yet<br><span class="dd-hint">Save a config (auto group of one) or Save group.</span></div>';
        return;
      }
      list.forEach(function(g){
        var row = document.createElement('div');
        row.className = 'lp-dd-item';
        var meta = document.createElement('div');
        meta.className = 'meta';
        meta.innerHTML = '<div class="name"></div><div class="sub"></div>';
        var nameEl = meta.querySelector('.name');
        nameEl.textContent = g.name || g.id;
        var names = (g.configIds || []).map(function(id){
          return (byId[id] && byId[id].name) || id;
        });
        var n = names.length;
        meta.querySelector('.sub').textContent = (n === 1 ? 'Group of one · ' : (n + ' configs · ')) + names.join(', ');
        var acts = document.createElement('div');
        acts.className = 'acts';
        // One open handler shared by the ✔ button and the clickable group name.
        /* Opens every config in a group. */
        function onOpenGroup(e){
          e.stopPropagation();
          closeAllDropdowns();
          openGroupWindows(g);
        }
        nameEl.classList.add('lp-grp-open');
        nameEl.title = 'Open group';
        nameEl.setAttribute('role', 'button');
        nameEl.setAttribute('tabindex', '0');
        nameEl.addEventListener('click', onOpenGroup);
        nameEl.addEventListener('keydown', function(e){
          if(e.key === 'Enter' || e.key === ' '){ e.preventDefault(); onOpenGroup(e); }
        });
        var openBtn = document.createElement('button');
        openBtn.type = 'button';
        openBtn.className = 'lp-ico lp-ico-open';
        openBtn.textContent = '\u2714';
        openBtn.title = 'Open group';
        openBtn.setAttribute('aria-label', 'Open group');
        openBtn.addEventListener('click', onOpenGroup);
        var editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'lp-ico lp-ico-edit';
        editBtn.textContent = '\u270E';
        editBtn.title = 'Edit group';
        editBtn.setAttribute('aria-label', 'Edit group');
        editBtn.addEventListener('click', function(e){
          e.stopPropagation();
          closeAllDropdowns();
          promptEditGroup(g.id);
        });
        var delBtn = document.createElement('button');
        delBtn.type = 'button';
        delBtn.className = 'danger lp-ico lp-ico-del';
        delBtn.textContent = '\u2716';
        delBtn.title = 'Delete group';
        delBtn.setAttribute('aria-label', 'Delete group');
        delBtn.addEventListener('click', function(e){
          e.stopPropagation();
          saveGroups(loadGroups().filter(function(x){ return x.id !== g.id; }));
          renderGroupsMenu();
          toast('Deleted group');
        });
        acts.appendChild(openBtn);
        acts.appendChild(editBtn);
        acts.appendChild(delBtn);
        row.appendChild(meta);
        row.appendChild(acts);
        groupsMenu.appendChild(row);
      });
    }
    /* Lists config URLs the browser refused to open. */
    /* Shown only when the host reports a real open-group failure. */
    function showBlockedLinks(blocked){
      openModal({
        title: 'Windows not opened',
        hint: 'The cockpit host could not open one or more config windows. Check the list, then try Open group again, or use Configs then Load in this window.',
        buildBody: function(body){
          var ul = document.createElement('div');
          ul.className = 'link-list';
          blocked.forEach(function(b){
            var row = document.createElement('div');
            row.textContent = (b.name || b.id);
            ul.appendChild(row);
          });
          body.appendChild(ul);
        },
        actions: [{ label: 'Close', primary: true }]
      });
    }
    /* Opens one full cockpit window per config in the group (host message). */
    function openGroupWindows(group){
      var ids = (group && group.configIds) || [];
      if(!ids.length){
        toast('Group has no configs');
        return;
      }
      var byId = {};
      loadConfigs().forEach(function(c){ byId[c.id] = c; });
      var host = window.chrome && window.chrome.webview;
      if(host && typeof host.postMessage === 'function'){
        try {
          host.postMessage({ type: 'adc-open-config-windows', ids: ids });
          toast('Opening ' + ids.length + ' cockpit window(s)…');
          return;
        } catch(_){}
      }
      // Fallback when no host bridge: try window.open (usually blocked in WV2).
      var blocked = [];
      var opened = 0;
      ids.forEach(function(id){
        var url = configUrl(id);
        var name = (byId[id] && byId[id].name) || id;
        var w = null;
        try { w = window.open(url, 'lp-cfg-' + id); } catch(_){ w = null; }
        if(!w) blocked.push({ id: id, name: name, url: url });
        else opened++;
      });
      if(blocked.length){
        toast('Opened ' + opened + '; ' + blocked.length + ' not opened - see list');
        showBlockedLinks(blocked);
      } else {
        toast('Opened group — ' + opened + ' window(s)');
      }
    }

    /* Host replies after adc-open-config-windows. */
    window.addEventListener('adc-host', function(ev){
      var d = ev && ev.detail;
      if(!d || d.type !== 'adc-open-config-windows-result') return;
      var opened = d.opened || 0;
      var failed = d.failed || [];
      if(failed.length){
        var byId = {};
        loadConfigs().forEach(function(c){ byId[c.id] = c; });
        showBlockedLinks(failed.map(function(id){ return { id: id, name: (byId[id] && byId[id].name) || id }; }));
        toast('Opened ' + opened + '; ' + failed.length + ' not opened - see list');
      } else {
        toast('Opened group — ' + opened + ' window(s)');
      }
    });
    /* Asks for a name and stores the current stage. */
    function promptSaveConfig(){
      openModal({
        title: 'Save config',
        hint: 'Snapshot of the ACTIVE tab (' + (TAB_LABELS[stageTab] || stageTab) + '): layout, apps, pane sizes, tray. Also creates/updates a matching Open group of one (same name) — start desk via Open group ▾.',
        buildBody: function(body){
          var lab = document.createElement('label');
          lab.textContent = 'Name';
          lab.setAttribute('for', 'lpCfgName');
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.id = 'lpCfgName';
          inp.placeholder = 'e.g. Morning Money';
          inp.value = '';
          body.appendChild(lab);
          body.appendChild(inp);
        },
        actions: [
          { label: 'Cancel' },
          { label: 'Save', primary: true, onClick: function(){
            var inp = document.getElementById('lpCfgName');
            var name = (inp && inp.value || '').trim();
            if(!name){ toast('Enter a name'); return true; }
            var list = loadConfigs();
            var exact = null;
            var folded = null;
            var key = name.toLowerCase();
            for(var i=0;i<list.length;i++){
              var stored = String(list[i].name == null ? '' : list[i].name).trim();
              if(!exact && stored === name) exact = list[i];
              if(!folded && stored.toLowerCase() === key) folded = list[i];
            }
            var existing = exact || folded;
            if(!existing){
              var cfg = captureConfig(name, null);
              list.push(cfg);
              saveConfigs(list);
              syncSoloGroup(cfg);
              renderConfigsMenu();
              renderGroupsMenu();
              toast('Saved “' + name + '” (+ Open group of one)');
              return;
            }
            openModal({
              title: 'Replace config',
              hint: 'A config with this name already exists.',
              actions: [
                { label: 'Cancel' },
                { label: 'Save as new', onClick: function(){
                  var fresh = loadConfigs();
                  var created = captureConfig(name, null);
                  fresh.push(created);
                  saveConfigs(fresh);
                  syncSoloGroup(created);
                  renderConfigsMenu();
                  renderGroupsMenu();
                  toast('Saved “' + name + '” as new');
                }},
                { label: 'Replace', primary: true, onClick: function(){
                  var next = loadConfigs();
                  var replaced = captureConfig(name, existing.id);
                  for(var j=0;j<next.length;j++){
                    if(next[j].id === existing.id){ next[j] = replaced; break; }
                  }
                  saveConfigs(next);
                  syncSoloGroup(replaced);
                  renderConfigsMenu();
                  renderGroupsMenu();
                  toast('Replaced existing config “' + name + '”');
                }}
              ]
            });
            return true;
          }}
        ]
      });
    }
    /* Renames one saved config. */
    function promptRenameConfig(id, oldName){
      openModal({
        title: 'Rename config',
        buildBody: function(body){
          var lab = document.createElement('label');
          lab.textContent = 'Name';
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.id = 'lpCfgRename';
          inp.value = oldName || '';
          body.appendChild(lab);
          body.appendChild(inp);
        },
        actions: [
          { label: 'Cancel' },
          { label: 'Rename', primary: true, onClick: function(){
            var inp = document.getElementById('lpCfgRename');
            var name = (inp && inp.value || '').trim();
            if(!name){ toast('Enter a name'); return true; }
            var list = loadConfigs();
            var found = false;
            for(var i=0;i<list.length;i++){
              if(list[i].id === id){
                list[i].name = name;
                list[i].savedAt = new Date().toISOString();
                found = true;
                break;
              }
            }
            if(!found){ toast('Config not found'); return; }
            saveConfigs(list);
            var renamed = findConfig(id);
            if(renamed) syncSoloGroup(renamed);
            renderConfigsMenu();
            renderGroupsMenu();
            toast('Renamed');
          }}
        ]
      });
    }
    /* Asks for a name and which configs belong in a group. */
    function promptSaveGroup(){
      var cfgs = loadConfigs();
      if(cfgs.length < 1){
        toast('Save at least one config first');
        return;
      }
      /* Save Group rule: 1+ configs checked AND the name has at least one letter (any language). */
      var saveGrpBtn = null;
      function saveGroupRuleOk(){ /* rule only, no side effects */
        var nameEl = document.getElementById('lpGrpName');
        var nm = (nameEl && nameEl.value || '').trim();
        var picked = document.querySelectorAll('#lpGrpChecks input[type=checkbox]:checked').length;
        return picked > 0 && /\p{L}/u.test(nm);
      }
      function validateSaveGroup(){
        if(saveGrpBtn) saveGrpBtn.disabled = !saveGroupRuleOk();
      }
      openModal({
        title: 'Save config group',
        hint: 'Pick 1+ configs. Open always goes through a group — one window.open(?config=id) per member (N=1 same loop). Open group asks the host for one cockpit window per config so you can place them on your monitors.',
        buildBody: function(body){
          var lab = document.createElement('label');
          lab.textContent = 'Group name';
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.id = 'lpGrpName';
          inp.placeholder = 'e.g. Desk dual';
          inp.addEventListener('input', validateSaveGroup);
          body.appendChild(lab);
          body.appendChild(inp);
          var clab = document.createElement('label');
          clab.textContent = 'Configs in this group';
          body.appendChild(clab);
          var list = document.createElement('div');
          list.className = 'check-list';
          list.id = 'lpGrpChecks';
          cfgs.forEach(function(c){
            var row = document.createElement('label');
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.value = c.id;
            row.appendChild(cb);
            var span = document.createElement('span');
            span.textContent = c.name || c.id;
            row.appendChild(span);
            list.appendChild(row);
          });
          list.addEventListener('change', validateSaveGroup);
          body.appendChild(list);
        },
        actions: [
          { label: 'Cancel' },
          { label: 'Save group', primary: true, onClick: function(){
            if(!saveGroupRuleOk()){ validateSaveGroup(); toast('Pick at least 1 config and use a name with a letter'); return true; }
            var inp = document.getElementById('lpGrpName');
            var name = (inp && inp.value || '').trim();
            var checks = document.querySelectorAll('#lpGrpChecks input[type=checkbox]:checked');
            var ids = [];
            checks.forEach(function(cb){ ids.push(cb.value); });
            var groups = loadGroups();
            var existing = null;
            for(var gi=0;gi<groups.length;gi++){
              if(groups[gi].name === name){ existing = groups[gi]; break; }
            }
            if(existing){
              existing.configIds = ids;
            } else {
              groups.push({ id: uid('grp'), name: name, configIds: ids });
            }
            saveGroups(groups);
            renderGroupsMenu();
            toast('Saved group “' + name + '” (' + ids.length + ')');
          }}
        ]
      });
      saveGrpBtn = modalActions ? modalActions.querySelector('button.primary') : null;
      validateSaveGroup();
    }
    /** Edit an existing group: name, member configs (from lp-configs) and their open order. */
    function promptEditGroup(groupId){
      var grp = findGroup(groupId);
      if(!grp){ toast('Group not found'); return; }
      var cfgs = loadConfigs();
      var byId = {};
      cfgs.forEach(function(c){ byId[c.id] = c; });
      var memberIds = (grp.configIds || []).slice();
      // Members first (in open order), then the remaining saved configs.
      var rows = memberIds.map(function(id){ return { id: id, on: true }; });
      cfgs.forEach(function(c){ if(memberIds.indexOf(c.id) < 0) rows.push({ id: c.id, on: false }); });
      openModal({
        title: 'Edit group',
        hint: 'Rename, pick member configs, and set open order (one window per checked config, top to bottom).',
        buildBody: function(body){
          var lab = document.createElement('label');
          lab.textContent = 'Group name';
          lab.setAttribute('for', 'lpGrpEditName');
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.id = 'lpGrpEditName';
          inp.value = grp.name || '';
          body.appendChild(lab);
          body.appendChild(inp);
          var clab = document.createElement('label');
          clab.textContent = 'Configs in this group (checked = member; order = open order)';
          body.appendChild(clab);
          var list = document.createElement('div');
          list.className = 'check-list';
          list.id = 'lpGrpEditChecks';
          rows.forEach(function(r){
            var row = document.createElement('label');
            row.className = 'lp-grp-edit-row';
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.value = r.id;
            cb.checked = r.on;
            row.appendChild(cb);
            var span = document.createElement('span');
            span.className = 'lbl';
            span.textContent = byId[r.id] ? (byId[r.id].name || r.id) : (r.id + ' (missing config)');
            row.appendChild(span);
            [['\u2191', 'Move up', -1], ['\u2193', 'Move down', 1]].forEach(function(m){
              var b = document.createElement('button');
              b.type = 'button';
              b.textContent = m[0];
              b.title = m[1];
              b.setAttribute('aria-label', m[1]);
              b.addEventListener('click', function(e){
                e.preventDefault();
                e.stopPropagation();
                if(m[2] < 0 && row.previousElementSibling) list.insertBefore(row, row.previousElementSibling);
                else if(m[2] > 0 && row.nextElementSibling) list.insertBefore(row.nextElementSibling, row);
              });
              row.appendChild(b);
            });
            list.appendChild(row);
          });
          body.appendChild(list);
        },
        actions: [
          { label: 'Cancel' },
          { label: 'Save', primary: true, onClick: function(){
            var inp = document.getElementById('lpGrpEditName');
            var name = (inp && inp.value || '').trim();
            if(!name){ toast('Enter a group name'); return true; }
            var groups = loadGroups();
            var clash = groups.some(function(x){
              return x.id !== groupId && String(x.name || '').trim().toLowerCase() === name.toLowerCase();
            });
            if(clash){ toast('A group named “' + name + '” already exists'); return true; }
            var ids = [];
            document.querySelectorAll('#lpGrpEditChecks input[type=checkbox]').forEach(function(cb){
              if(cb.checked) ids.push(cb.value);
            });
            if(ids.length < 1){ toast('Pick at least 1 config'); return true; }
            var found = false;
            for(var i=0;i<groups.length;i++){
              if(groups[i].id === groupId){
                groups[i].name = name;
                groups[i].configIds = ids;
                found = true;
                break;
              }
            }
            if(!found){ toast('Group not found'); return; }
            saveGroups(groups);
            renderGroupsMenu();
            toast('Saved group “' + name + '” (' + ids.length + ')');
          }}
        ]
      });
    }
    /* Stores the sample Morning Money and TradingView configs once. */
    function seedIfEmpty(){
      var configs = loadConfigs();
      var groups = loadGroups();
      if(configs.length) return;
      var morning = {
        id: 'cfg_seed_morning',
        name: 'Morning Money',
        savedAt: new Date().toISOString(),
        layout: 'half',
        slots: ['YNAB'],
        sizes: cloneSizes(DEFAULT_STAGE_SIZES.half),
        trayWidth: DEFAULT_TRAY_WIDTH,
        trayCollapsed: false,
        tab: 'launch'
      };
      var trading = {
        id: 'cfg_seed_tv',
        name: 'Single TradingView',
        savedAt: new Date().toISOString(),
        layout: 'single',
        slots: ['TradingView'],
        sizes: cloneSizes(DEFAULT_STAGE_SIZES.single),
        trayWidth: DEFAULT_TRAY_WIDTH,
        trayCollapsed: false,
        tab: 'launch'
      };
      saveConfigs([morning, trading]);
      if(!groups.length){
        saveGroups([
          { id: 'grp_seed_morning', name: 'Morning Money', configIds: [morning.id] },
          { id: 'grp_seed_tv', name: 'Single TradingView', configIds: [trading.id] },
          { id: 'grp_seed_desk', name: 'Desk dual', configIds: [morning.id, trading.id] }
        ]);
      }
    }

    var saveCfgBtn = document.getElementById('lpSaveConfig');
    var configsBtn = document.getElementById('lpConfigsBtn');
    var saveGrpBtn = document.getElementById('lpSaveGroup');
    var groupsBtn = document.getElementById('lpGroupsBtn');
    if(saveCfgBtn) saveCfgBtn.addEventListener('click', function(e){ e.stopPropagation(); promptSaveConfig(); });
    if(configsBtn) configsBtn.addEventListener('click', function(e){
      e.stopPropagation();
      renderConfigsMenu();
      toggleDropdown(configsDd);
    });
    if(saveGrpBtn) saveGrpBtn.addEventListener('click', function(e){ e.stopPropagation(); promptSaveGroup(); });
    if(groupsBtn) groupsBtn.addEventListener('click', function(e){
      e.stopPropagation();
      renderGroupsMenu();
      toggleDropdown(groupsDd);
    });

    seedIfEmpty();
    // Backfill: every config gets a group-of-one for Open group
    loadConfigs().forEach(function(c){
      var hasSolo = loadGroups().some(function(g){ var ids = g.configIds || []; return ids.length === 1 && ids[0] === c.id; });
      if(!hasSolo) syncSoloGroup(c);
    });
    renderConfigsMenu();
    renderGroupsMenu();

    window.addEventListener('resize', function(){
      if(!stageDrag) refreshStageGutters();
      scheduleSlotReport();
    });
    if(window.ResizeObserver){
      var slotObserver = new ResizeObserver(function(){ scheduleSlotReport(); });
      slotObserver.observe(document.body);
      var slotMain = document.getElementById('appMain');
      if(slotMain) slotObserver.observe(slotMain);
    }

    /* Toolbar row collapse (localStorage 'lp-toolbar-collapsed') */
    var TOOLBAR_COLLAPSED_KEY = 'lp-toolbar-collapsed';
    var tbToggle = document.getElementById('lpToolbarToggle');
    /* Hides or shows the layout toolbar. */
    function setToolbarCollapsed(c, persist){
      document.body.classList.toggle('toolbar-collapsed', !!c);
      if(tbToggle){
        tbToggle.setAttribute('aria-expanded', c ? 'false' : 'true');
        tbToggle.title = c ? 'Show the tabs and layout toolbars' : 'Hide the tabs and layout toolbars';
        tbToggle.querySelector('.tt-chev').textContent = c ? '▾' : '▴';
        tbToggle.querySelector('.tt-lbl').textContent = c ? 'Show toolbars' : 'Hide toolbars';
      }
      if(c) closeAllDropdowns();
      if(persist){ try { localStorage.setItem(TOOLBAR_COLLAPSED_KEY, c ? 'true' : 'false'); } catch(_) {} }
      setTimeout(function(){ try { refreshStageGutters(); } catch(_){} scheduleSlotReport(); }, 30);
    }
    var tbSaved = null;
    try { tbSaved = localStorage.getItem(TOOLBAR_COLLAPSED_KEY); } catch(_) {}
    setToolbarCollapsed(tbSaved === 'true', false);
    if(tbToggle) tbToggle.addEventListener('click', function(e){
      e.stopPropagation();
      setToolbarCollapsed(!document.body.classList.contains('toolbar-collapsed'), true);
    });

    switchStage(currentTab());

    // URL ?config=id — restore after DOM/render ready
    var urlCfgId = configIdFromUrl();
    if(urlCfgId){
      var urlCfg = findConfig(urlCfgId);
      if(urlCfg){
        applyConfig(urlCfg);
        toast('Restored “' + (urlCfg.name || urlCfgId) + '”');
      } else {
        toast('Config not found: ' + urlCfgId);
      }
    }
    window.__ADC = window.__ADC || {};
    window.__ADC.lp = {
      LAYOUTS: LAYOUTS,
      DEFAULT_STAGE_SIZES: DEFAULT_STAGE_SIZES,
      ICONS: ICONS,
      TAB_IDS: TAB_IDS,
      TAB_LABELS: TAB_LABELS,
      stages: stages,
      applyStageDefs: applyStageDefs,
      refreshIcons: refreshIcons,
      bindTrayTiles: bindTrayTiles,
      bindRail: bindRail,
      bindLayoutButtons: bindLayoutButtons,
      ensureTabStage: ensureTabStage,
      removeTabStage: removeTabStage,
      defaultStage: defaultStage,
      render: render,
      switchStage: switchStage,
      syncLayoutButtons: syncLayoutButtons,
      openModal: openModal,
      closeModal: closeModal,
      toast: toast,
      setLayout: setLayout,
      persistStages: persistStages,
      /* 2026-10-09 v1.5.0 (adc-setups): used by the pane and card right-click menus */
      addPane: (typeof addPane === 'function') ? addPane : function(){ var b = document.getElementById('lpAddPane'); if(b) b.click(); },
      movePane: function(from, to){ if(from === to || !state.slots[from]) return; state.drag = { source: 'slot', from: from, app: state.slots[from] }; handleDrop(to); },
      activeStage: function(){ return stageTab; }
    };
  }
  window.__initLaunchPad = initLaunchPad;
})();

/* Loads the JSON files and starts the cockpit from that data. adc-page-data is not applied. */
(function bootAdvancedDirectorCockpit(){
  var OVERLAY_KEY = "adc-page-data";
  var editing = false;
  var LOAD_NOTE = "Couldn't load the page data from www\\data. Check that cards.json, groups.json, tabs.json, and stages.json are valid JSON, then restart the app.";

  /* Reads one JSON file. Rejects when the server or file:// blocks it. */
  function fetchJson(name){
    return fetch("data/" + name + ".json", { cache: "no-store" }).then(function(res){
      if(!res.ok) throw new Error(name);
      return res.json();
    });
  }

  /* Leaves the tray empty and explains the file:// limit. */
  function showLoadNote(){
    var root = document.querySelector(".lp-tray-scroll");
    if(!root) return;
    root.textContent = "";
    var p = document.createElement("p");
    p.className = "adc-load-note";
    p.textContent = LOAD_NOTE;
    root.appendChild(p);
  }

  /* Returns a saved full copy of the page data, or null. */
  function readOverlay(){
    try {
      var o = JSON.parse(localStorage.getItem(OVERLAY_KEY) || "null");
      if(!o || !Array.isArray(o.cards) || !Array.isArray(o.groups) || !Array.isArray(o.tabs)) return null;
      if(!o.stages || !Array.isArray(o.stages.layouts)) return null;
      if(Object.prototype.hasOwnProperty.call(o, "rail")){
        delete o.rail;
        o._adcStripRail = true;
      }
      return o;
    } catch(e){ return null; }
  }

  var LIVE_FILES = ["cards", "groups", "tabs", "stages"];
  var SAVE_FILES = ["cards", "groups", "tabs", "stages"];
  var LIVE_OPTIONAL = { tools: true };
  /* ===== Live data files (2026-10-05) =====
     1. Every load reads data/*.json with cache "no-store".
     2. While the page is open, the files are re-read every few seconds. A change is drawn into the
        tray, rail, tabs, and layouts without a page reload. Open panes are kept. If a dialog, a drag,
        or a text box is in use, a toast says the files changed and the new data loads after that ends.
     3. Before any write-back the files are re-read. If one differs from the exact text last loaded
        (or last saved), nothing is written: the new data is loaded and a toast asks to redo the edit. */
  var LIVE_POLL_MS = 3000;
  var LIVE_TOAST_MS = 6000;
  var liveBase = {};
  var liveSaving = false;
  var liveSaveQueued = false;
  var liveApplying = false;
  var livePolling = false;
  var livePendingToast = false;
  var liveTimer = 0;
  var liveStats = { polls: 0, applied: 0, deferred: 0, saves: 0, blocked: 0, failed: 0 };

  /* Reads one data file as exact text, never from the HTTP cache. */
  function fetchText(name){
    return fetch("data/" + name + ".json", { cache: "no-store" }).then(function(res){
      if(!res.ok) throw new Error(name);
      return res.text();
    });
  }

  /* Reads a list of data files. An optional file that fails reads as null. */
  function fetchTexts(names){
    return Promise.all(names.map(function(n){
      var p = fetchText(n);
      return LIVE_OPTIONAL[n] ? p.catch(function(){ return null; }) : p;
    })).then(function(list){
      var out = {};
      names.forEach(function(n, i){ out[n] = list[i]; });
      return out;
    });
  }

  /* Builds the page model from file texts. Throws when a required file is not valid JSON. */
  function seedFromTexts(t){
    var cards = JSON.parse(t.cards), groups = JSON.parse(t.groups), tabs = JSON.parse(t.tabs), st = JSON.parse(t.stages);
    var seed = {
      cards: (cards && cards.cards) || [],
      groups: (groups && groups.groups) || [],
      tabs: (tabs && tabs.tabs) || [],
      stages: {
        nativeSlot: (st && st.nativeSlot) || "@native",
        layouts: (st && st.layouts) || [],
        defaults: (st && st.defaults) || {}
      }
    };
    if(LIVE_FILES.indexOf("tools") >= 0){
      var tools = null;
      try { tools = t.tools == null ? null : JSON.parse(t.tools); } catch(e){ tools = null; }
      seed.tools = (tools && tools.tools) || [];
    }
    return seed;
  }

  function rememberBase(texts){
    Object.keys(texts).forEach(function(n){ liveBase[n] = texts[n]; });
  }

  /* Names of files whose text differs from the remembered baseline. */
  function changedFiles(texts){
    return Object.keys(texts).filter(function(n){ return texts[n] !== liveBase[n]; });
  }

  /* Shows a toast that stays long enough to read. */
  function liveToast(msg){
    var el = document.getElementById("lpToast");
    if(!el){
      var lp = window.__ADC && window.__ADC.lp;
      if(lp && typeof lp.toast === "function") lp.toast(msg);
      return;
    }
    el.textContent = msg;
    el.classList.add("show");
    clearTimeout(el._adcLiveTimer);
    el._adcLiveTimer = setTimeout(function(){
      if(el.textContent === msg) el.classList.remove("show");
    }, LIVE_TOAST_MS);
  }

  /* True while Director is in a dialog, dragging, or typing in a page field. */
  function liveBusy(){
    if(liveSaving || liveSaveQueued) return true;
    var modal = document.getElementById("lpModal");
    if(modal && modal.classList.contains("show")) return true;
    if(document.body && document.body.classList.contains("adc-dragging")) return true;
    if(window.__adcTabDrag) return true;
    if(window.__adcGroupDrag) return true;
    try { if(typeof draggingTabId !== "undefined" && draggingTabId) return true; } catch(e){}
    if(document.querySelector(".tab-dragging, .lp-tile.dragging, .split.dragging, .lp-splitter.dragging")) return true;
    var ae = document.activeElement;
    if(ae && ae.tagName && /^(INPUT|TEXTAREA|SELECT)$/.test(ae.tagName) && !(ae.closest && ae.closest(".lp-stage-grid"))) return true;
    if(ae && ae.isContentEditable) return true;
    return false;
  }

  /* Draws new file data into the open page. Panes are rebuilt only when stages.json changed. */
  function liveApply(texts, seed){
    var old = window.__ADC && window.__ADC.model;
    var changed = changedFiles(texts);
    rememberBase(texts);
    if(!old){ return; }
    var tabsChanged = changed.indexOf("tabs") >= 0;
    var layoutsChanged = changed.indexOf("stages") >= 0;
    var activeView = document.querySelector(".view.active");
    var activeId = activeView && activeView.id ? activeView.id.replace(/^view-/, "") : "";
    var oldTabIds = (old.tabs || []).map(function(t){ return t && t.id; });
    liveApplying = true;
    try {
      window.__ADC.seed = seed;
      window.__ADC.model = seed;
      var lp = window.__ADC.lp;
      if(tabsChanged){
        var newIds = (seed.tabs || []).map(function(t){ return t && t.id; });
        if(lp){
          (seed.tabs || []).forEach(function(t){
            if(!t || !t.id) return;
            if(typeof syncTabRuntime === "function") syncTabRuntime(t, oldTabIds.indexOf(t.id) < 0);
          });
          oldTabIds.forEach(function(id){
            if(id && newIds.indexOf(id) < 0 && lp.removeTabStage) lp.removeTabStage(id);
          });
        }
        renderTabs();
        if(window.__adcBindViewTabs) window.__adcBindViewTabs();
        var stillActive = document.querySelector(".view.active");
        var keepId = stillActive && stillActive.id ? stillActive.id.replace(/^view-/, "") : activeId;
        if(keepId && document.querySelector('#tabs .tab[data-view="' + keepId + '"]')){
          document.querySelectorAll("#tabs .tab[data-view]").forEach(function(ch){
            ch.classList.toggle("active", ch.getAttribute("data-view") === keepId);
          });
        }
        if(editing){
          document.querySelectorAll("#tabs .tab[data-view]").forEach(function(chip){ chip.setAttribute("draggable", "true"); });
        }
      }
      if(layoutsChanged) renderLayoutButtons();
      renderRail();
      renderTray();
      if(lp){
        lp.refreshIcons();
        lp.bindTrayTiles();
        if(lp.bindRail) lp.bindRail();
        if(layoutsChanged){
          lp.applyStageDefs();
          lp.bindLayoutButtons();
        }
        if(lp.syncLayoutButtons) lp.syncLayoutButtons();
        if(layoutsChanged) lp.render();
      }
      bindGroupCollapse();
      if(editing){
        clearEditControls();
        mountEditControls();
      }
      equalizeTrayCards(true);
      liveStats.applied++;
    } finally {
      liveApplying = false;
    }
    try { document.dispatchEvent(new CustomEvent("adc:data-reloaded", { detail: { files: changed } })); } catch(e){}
  }

  /* One poll: re-read every data file and apply a change, or hold it while Director is mid-edit. */
  function livePoll(){
    if(livePolling || liveSaving || liveSaveQueued || liveApplying) return Promise.resolve(false);
    if(!window.__ADC || !window.__ADC.model) return Promise.resolve(false);
    livePolling = true;
    liveStats.polls++;
    return fetchTexts(LIVE_FILES).then(function(texts){
      if(liveSaving || liveSaveQueued) return false;
      if(!changedFiles(texts).length){ livePendingToast = false; return false; }
      var seed;
      try { seed = seedFromTexts(texts); } catch(e){ return false; }
      if(liveBusy()){
        liveStats.deferred++;
        if(!livePendingToast){
          livePendingToast = true;
          liveToast("The data files changed on disk. The new data loads when you finish this edit.");
        }
        return false;
      }
      var hadToast = livePendingToast;
      livePendingToast = false;
      liveApply(texts, seed);
      if(hadToast) liveToast("Loaded the new data from disk.");
      return true;
    }).catch(function(){ return false; }).then(function(r){
      livePolling = false;
      return r;
    });
  }

  function startLivePoll(){
    if(liveTimer) return;
    liveTimer = setInterval(livePoll, LIVE_POLL_MS);
    document.addEventListener("visibilitychange", function(){ if(!document.hidden) livePoll(); });
    window.addEventListener("focus", function(){ livePoll(); });
  }

  /* Writes cards, groups, tabs, and stages to the JSON files. Does not write adc-page-data. */
  function adcFileSaveFailed(){
    var lp = window.__ADC && window.__ADC.lp;
    if(lp && typeof lp.toast === "function"){
      lp.toast("The file was not saved");
      return;
    }
    var el = document.getElementById("lpToast");
    if(!el) return;
    el.textContent = "The file was not saved";
    el.classList.add("show");
    clearTimeout(el._adcSaveTimer);
    el._adcSaveTimer = setTimeout(function(){ el.classList.remove("show"); }, 1800);
  }

  function sameJson(a, b){
    try { return JSON.stringify(a) === JSON.stringify(b); } catch(e){ return false; }
  }

  /* Re-reads the files, compares them to the baseline, and only then posts the save. */
  function guardedSave(){
    var m = window.__ADC && window.__ADC.model;
    if(!m) return Promise.resolve("none");
    var body;
    try { body = pagePayload(m); }
    catch(err){ adcFileSaveFailed(); liveStats.failed++; return Promise.resolve("failed"); }
    return fetchTexts(SAVE_FILES).then(function(now){
      var moved = changedFiles(now);
      if(moved.length){
        liveStats.blocked++;
        return fetchTexts(LIVE_FILES).then(function(texts){
          var seed = seedFromTexts(texts);
          liveApply(texts, seed);
          liveToast("The data files changed on disk, so your save was not applied. The new data is loaded. Redo the edit.");
          return "blocked";
        });
      }
      var payload = JSON.stringify(body);
      return fetch("/adc-data", {
        method: "POST",
        cache: "no-store",
        headers: { "Content-Type": "application/json" },
        body: payload
      }).then(function(res){
        if(!res.ok) throw new Error(String(res.status));
        liveStats.saves++;
        return fetchTexts(LIVE_FILES).then(function(after){
          var seed = null;
          try { seed = seedFromTexts(after); } catch(e){ seed = null; }
          var matches = !!seed && sameJson(seed.cards, body.cards) && sameJson(seed.groups, body.groups)
            && sameJson(seed.tabs, body.tabs) && sameJson(seed.stages, body.stages);
          if(matches){
            rememberBase(after);
          } else if(seed){
            liveApply(after, seed);
          }
          return "saved";
        }, function(){ return "saved"; });
      });
    }).catch(function(){
      liveStats.failed++;
      adcFileSaveFailed();
      return "failed";
    });
  }

  /* Runs one save at a time. Saves asked for while one runs collapse into one more save. */
  var liveLastSave = Promise.resolve("none");
  function saveOverlay(){
    if(liveApplying) return liveLastSave;
    if(!window.__ADC || !window.__ADC.model) return liveLastSave;
    if(liveSaving){
      liveSaveQueued = true;
      return liveLastSave;
    }
    liveSaving = true;
    liveLastSave = guardedSave().then(function(result){
      liveSaving = false;
      if(liveSaveQueued){
        liveSaveQueued = false;
        if(result !== "blocked") return saveOverlay();
      }
      return result;
    });
    return liveLastSave;
  }
  window.__adcSaveOverlay = saveOverlay;
  window.__adcLive = {
    base: liveBase,
    stats: liveStats,
    poll: livePoll,
    busy: liveBusy,
    lastSave: function(){ return liveLastSave; }
  };

    function pagePayload(m){
      return {
        groups: (m.groups || []).map(function(g){
          return {
            id: g.id,
            parentId: g.parentId ? g.parentId : null,
            title: g.title || "",
            order: g.order || 0,
            defaultCollapsed: !!g.defaultCollapsed,
            showInRail: g.showInRail !== false
          };
        }),
        cards: (m.cards || []).map(function(c){
          var o = {
            id: c.id,
            groupId: c.groupId || "",
            order: c.order || 0,
            app: c.app || "",
            name: c.name || "",
            description: c.description || "",
            icon: c.icon || "",
            color: c.color || ""
          };
          if(c.url) o.url = c.url;
          if(c.iconImage) o.iconImage = c.iconImage;
          if(c.iconTile === "dark" || c.iconTile === "none") o.iconTile = c.iconTile; /* adc-icontile: light = omitted */
          if(c.args) o.args = c.args;
          if(c.browserIdentity) o.browserIdentity = c.browserIdentity; /* r7.1 per-card browser identity */
          if(c.profile) o.profile = c.profile;
          return o;
        }),
        tabs: m.tabs,
        stages: m.stages
      };
    }

  window.__adcPagePayload = pagePayload;


  /* Sorts a copy by the order field. */
  function byOrder(list){
    return (list || []).slice().sort(function(a, b){ return (a.order || 0) - (b.order || 0); });
  }

  /* Finds a record by id. */
  function findById(list, id){
    for(var i = 0; i < (list || []).length; i++) if(list[i].id === id) return list[i];
    return null;
  }

  /* Builds an id that does not collide with records already in the list. */
  function mintId(list, base){
    var stem = String(base || "item").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "item";
    var used = {};
    (list || []).forEach(function(row){ if(row && row.id) used[row.id] = 1; });
    var id = stem, n = 2;
    while(used[id]){ id = stem + "-" + n; n++; }
    return id;
  }

  /* Parses a whole number, or returns the fallback. */
  function num(v, fallback){
    var n = parseInt(v, 10);
    return isFinite(n) ? n : fallback;
  }

  /* Parses a comma list of positive numbers. */
  function numList(v){
    return String(v || "").split(",").map(function(x){ return Number(String(x).trim()); }).filter(function(n){ return isFinite(n) && n > 0; });
  }

  /* Looks up a card glyph by app name. */
  function iconFor(app){
    var list = (window.__ADC && window.__ADC.model && window.__ADC.model.cards) || [];
    for(var i = 0; i < list.length; i++){
      if(list[i].app === app && list[i].icon) return list[i].icon;
    }
    return "";
  }

  /* Adds a labeled text box to a dialog. */
  function field(parent, label, value){
    var lab = document.createElement("label");
    lab.textContent = label;
    var input = document.createElement("input");
    input.type = "text";
    input.value = value == null ? "" : String(value);
    parent.appendChild(lab);
    parent.appendChild(input);
    return input;
  }

  /* Adds a labeled dropdown to a dialog. */
  function selectField(parent, label, options, value){
    var lab = document.createElement("label");
    lab.textContent = label;
    var sel = document.createElement("select");
    options.forEach(function(o){
      var opt = document.createElement("option");
      opt.value = o.value;
      opt.textContent = o.label;
      if(o.value === value) opt.selected = true;
      sel.appendChild(opt);
    });
    parent.appendChild(lab);
    parent.appendChild(sel);
    return sel;
  }

  /* Adds a checkbox row to a dialog. */
  function checkField(parent, label, checked){
    var lab = document.createElement("label");
    var input = document.createElement("input");
    input.type = "checkbox";
    input.checked = !!checked;
    lab.appendChild(input);
    lab.appendChild(document.createTextNode(" " + label));
    parent.appendChild(lab);
    return input;
  }

  /* Opens the shared dialog. Returning an error string keeps it open. */
  function ask(title, hint, build, onSave, onCancel){
    var lp = window.__ADC && window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    lp.openModal({
      title: title,
      hint: hint,
      buildBody: build,
      actions: [
        { label: "Cancel", onClick: function(){
          if(typeof onCancel === "function"){ onCancel(); return true; }
        } },
        { label: "Save", primary: true, onClick: function(){
          var err = onSave();
          if(err){ if(lp.toast) lp.toast(err); return true; }
        } }
      ]
    });
  }

  /* Asks before a delete. */
  function confirmBox(title, hint, onYes, onNo){
    var lp = window.__ADC && window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    lp.openModal({
      title: title,
      hint: hint,
      actions: [
        { label: "Cancel", onClick: function(){
          if(typeof onNo === "function"){ onNo(); return true; }
        } },
        { label: "Delete", primary: true, onClick: function(){
          var keep = onYes && onYes();
          if(keep === true) return true;
        } }
      ]
    });
  }

  /* Makes a small Edit or Delete control. Spans are used inside buttons. */
  function mini(label, onClick, danger, asSpan){
    var el = document.createElement(asSpan ? "span" : "button");
    if(!asSpan) el.type = "button";
    el.className = "adc-mini" + (danger ? " danger" : "");
    el.textContent = label;
    if(asSpan){ el.tabIndex = 0; el.setAttribute("role", "button"); }
    function fire(e){ e.preventDefault(); e.stopPropagation(); onClick(); }
    el.addEventListener("click", fire);
    el.addEventListener("keydown", function(e){ if(e.key === "Enter" || e.key === " ") fire(e); });
    return el;
  }


  /* Remembers which tab is being dragged on the strip or in the tab list. */
  var draggingTabId = "";
  var tabListBody = null;

  /* Next order is one past the current tabs, so a new tab goes at the end. */
  function nextTabOrder(){
    var max = 0;
    (window.__ADC.model.tabs || []).forEach(function(t){
      var n = parseInt(t.order, 10);
      if(isFinite(n) && n > max) max = n;
    });
    return max + 1;
  }

  /* Moves a tab in front of another and saves that order immediately. */
  function moveTabBefore(fromId, toId){
    if(!fromId || !toId || fromId === toId || !window.__ADC || !window.__ADC.model) return;
    draggingTabId = '';
    window.__adcTabDrag = false;
    var list = byOrder(window.__ADC.model.tabs);
    var from = -1, to = -1, i;
    for(i = 0; i < list.length; i++){
      if(list[i].id === fromId) from = i;
      if(list[i].id === toId) to = i;
    }
    if(from < 0 || to < 0) return;
    var item = list.splice(from, 1)[0];
    list.splice(to, 0, item);
    for(i = 0; i < list.length; i++) list[i].order = i + 1;
    saveOverlay();
    renderTabs();
    if(window.__adcBindViewTabs) window.__adcBindViewTabs();
    if(editing){
      clearEditControls();
      mountEditControls();
    }
    if(tabListBody && tabListBody.isConnected) paintTabList(tabListBody);
  }

  /* 2026-10-11 adc-v159: moves a tab before or after another tab and saves the order (tabs.json via saveOverlay).
     Used by the pointer drag on the tab strip below; returns false when nothing changed. */
  function moveTabTo(fromId, toId, after){
    if(!fromId || !toId || fromId === toId || !window.__ADC || !window.__ADC.model) return false;
    var list = byOrder(window.__ADC.model.tabs);
    var from = -1, to = -1, i;
    for(i = 0; i < list.length; i++){ if(list[i].id === fromId) from = i; }
    if(from < 0) return false;
    var item = list.splice(from, 1)[0];
    for(i = 0; i < list.length; i++){ if(list[i].id === toId) to = i; }
    if(to < 0) return false;
    list.splice(after ? to + 1 : to, 0, item);
    var changed = false;
    for(i = 0; i < list.length; i++){ if(list[i].order !== i + 1) changed = true; list[i].order = i + 1; }
    if(!changed) return false;
    draggingTabId = '';
    window.__adcTabDrag = false;
    saveOverlay();
    renderTabs();
    if(window.__adcBindViewTabs) window.__adcBindViewTabs();
    if(editing){
      clearEditControls();
      mountEditControls();
    }
    if(tabListBody && tabListBody.isConnected) paintTabList(tabListBody);
    return true;
  }
  window.__adcMoveTab = moveTabTo;

  /* Drag tabs on the strip. Drop saves order; a plain click still switches views. */
  function bindTabStripDrag(){
    var host = document.getElementById("tabs");
    if(!host || !window.__ADC || !window.__ADC.model) return;
    host.querySelectorAll(".tab[data-view]").forEach(function(chip){
      chip.setAttribute("draggable", (editing || chip.classList.contains("adc-gtab")) ? "true" : "false");
      if(!chip.getAttribute("title")) chip.title = "Drag to reorder tabs";
      if(chip.dataset.adcStripDrag) return;
      chip.dataset.adcStripDrag = "1";
      chip.addEventListener("dragstart", function(e){
        if(!editing && !chip.classList.contains("adc-gtab")){ e.preventDefault(); return; }
        if(e.target.closest && e.target.closest("button, .adc-mini")){ e.preventDefault(); return; }
        window.__adcTabDrag = true;
        draggingTabId = chip.getAttribute("data-view") || "";
        chip.classList.add("tab-dragging");
        try {
          e.dataTransfer.setData("text/plain", draggingTabId);
          e.dataTransfer.effectAllowed = "move";
        } catch(err){}
      });
      chip.addEventListener("dragend", function(){
        chip.classList.remove("tab-dragging");
        host.querySelectorAll(".tab-drop").forEach(function(el){ el.classList.remove("tab-drop"); });
        window.__adcTabDrag = false;
        draggingTabId = "";
      });
      chip.addEventListener("dragover", function(e){
        if(!draggingTabId) return;
        e.preventDefault();
        if(draggingTabId !== chip.getAttribute("data-view")) chip.classList.add("tab-drop");
      });
      chip.addEventListener("dragleave", function(){ chip.classList.remove("tab-drop"); });
      chip.addEventListener("drop", function(e){
        if(!draggingTabId) return;
        e.preventDefault();
        e.stopPropagation();
        chip.classList.remove("tab-drop");
        var fromId = draggingTabId;
        var toId = chip.getAttribute("data-view");
        if(fromId && fromId !== toId) window.__adcSuppressTabClick = true;
        moveTabBefore(fromId, toId);
      });
    });
  }

  /* Draws tab chips from the model and leaves the toolbar toggle in place. */
  function renderTabs(){
    var host = document.getElementById("tabs");
    var toggle = document.getElementById("lpTabsEnd") || document.getElementById("lpToolbarToggle");
    if(!host || !window.__ADC || !window.__ADC.model) return;
    if(!Array.isArray(window.__ADC.model.tabs)) window.__ADC.model.tabs = [];
    var current = host.querySelector(".tab.active");
    var currentId = current ? current.getAttribute("data-view") : "";
    host.querySelectorAll(".tab[data-view]").forEach(function(t){ t.remove(); });
    byOrder(window.__ADC.model.tabs).forEach(function(tab){
      var d = document.createElement("div");
      d.className = "tab" + (tab.group ? " adc-gtab" : "");
      d.setAttribute("data-view", tab.id);
      d.tabIndex = 0;
      var mark = document.createElement("span");
      mark.className = "tab-mark" + (tab.mark ? " " + tab.mark : "");
      mark.setAttribute("aria-hidden", "true");
      d.appendChild(mark);
      d.appendChild(document.createTextNode(tab.label || tab.id));
      if(toggle && toggle.parentNode === host) host.insertBefore(d, toggle);
      else host.appendChild(d);
    });
    var chips = host.querySelectorAll(".tab[data-view]");
    var savedId = "";
    try { savedId = localStorage.getItem("lp-active-tab") || ""; } catch(e){}
    var modelActive = "";
    window.__ADC.model.tabs.forEach(function(tab){ if(tab && tab.active) modelActive = tab.id; });
    function chipHas(id){
      var ok = false;
      chips.forEach(function(ch){ if(ch.getAttribute("data-view") === id) ok = true; });
      return ok;
    }
    var prefer = "";
    if(savedId && chipHas(savedId)) prefer = savedId;
    else if(modelActive && chipHas(modelActive)) prefer = modelActive;
    else if(currentId && chipHas(currentId)) prefer = currentId;
    var kept = false;
    chips.forEach(function(ch){
      if(prefer && ch.getAttribute("data-view") === prefer){ ch.classList.add("active"); kept = true; }
    });
    if(!kept){
      var seed = null;
      window.__ADC.model.tabs.forEach(function(tab){ if(tab && tab.active) seed = tab.id; });
      chips.forEach(function(ch){
        if(!kept && ch.getAttribute("data-view") === (seed || (chips[0] && chips[0].getAttribute("data-view")))){
          ch.classList.add("active");
          kept = true;
        }
      });
    }
    bindTabStripDrag();
  }

  /* Draws the layout toolbar buttons from the stage model. */
  function renderLayoutButtons(){
    var host = document.getElementById("lpLayoutBtns");
    if(!host) return;
    var prev = host.querySelector(".lp-layout-btn.active");
    var current = prev ? prev.getAttribute("data-layout") : "half";
    host.textContent = "";
    byOrder(window.__ADC.model.stages.layouts).forEach(function(L){
      var b = document.createElement("button");
      b.type = "button";
      b.className = "lp-layout-btn" + (L.id === current ? " active" : "");
      b.setAttribute("data-layout", L.id);
      b.title = L.title || L.label || L.id;
      var glyph = document.createElement("span");
      glyph.className = "lp-mini" + (L.mini ? " " + L.mini : "");
      var n = L.miniCells || L.count || 1;
      for(var i = 0; i < n; i++) glyph.appendChild(document.createElement("i"));
      b.appendChild(glyph);
      b.appendChild(document.createTextNode(" " + (L.label || L.id)));
      host.appendChild(b);
    });
  }


  function mintUuid(){
    if(window.crypto && typeof crypto.randomUUID === "function") return crypto.randomUUID();
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function(ch){
      var r = Math.random() * 16 | 0;
      var v = ch === "x" ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
  }

  function isUuid(id){
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(String(id || ""));
  }

  /* 2026-10-10 (adc-copycard): copies one card into the same group, right after the original. Pure (no DOM, no save).
     New id from mint (retried until unused). Every field is copied except id and any vault/login-linked key.
     The original keeps its order; later cards in the group move down only as far as needed. */
  var COPY_CARD_SKIP = /vault|login|cred|secret|passw|username|token|allowedhosts|autosubmit|autoadvance/i;
  function copyCardAfter(cards, card, mint){
    if(!cards || !card || cards.indexOf(card) < 0) return null;
    var used = {};
    cards.forEach(function(c){ if(c && c.id) used[c.id] = 1; });
    var id = mint();
    for(var tries = 0; used[id] && tries < 20; tries++) id = mint();
    if(used[id]) return null;
    var copy = {};
    Object.keys(card).forEach(function(k){
      if(k === "id" || COPY_CARD_SKIP.test(k)) return;
      var v = card[k];
      copy[k] = v && typeof v === "object" ? JSON.parse(JSON.stringify(v)) : v;
    });
    copy.id = id;
    var group = cards.filter(function(c){ return c && c.groupId === card.groupId; })
      .map(function(c, i){ return { c: c, i: i }; })
      .sort(function(a, b){ return ((Number(a.c.order) || 0) - (Number(b.c.order) || 0)) || (a.i - b.i); })
      .map(function(x){ return x.c; });
    var prev = (Number(card.order) || 0) + 1;
    copy.order = prev;
    group.slice(group.indexOf(card) + 1).forEach(function(c){
      if((Number(c.order) || 0) <= prev) c.order = prev + 1;
      prev = Number(c.order) || 0;
    });
    cards.splice(cards.indexOf(card) + 1, 0, copy);
    return copy;
  }

  /* Seed JSON nests child groups. The overlay stores the same tree as parentId, with no children arrays. */
  function normalizeGroups(list){
    var src = list || [];
    var nested = false;
    var i;
    for(i = 0; i < src.length; i++){
      if(src[i] && Array.isArray(src[i].groups) && src[i].groups.length) nested = true;
    }
    var out = [];
    function emit(g, parentFromTree){
      if(!g || !g.id) return;
      var pid = null;
      if(nested) pid = parentFromTree || null;
      else if(g.parentId) pid = g.parentId;
      out.push({
        id: g.id,
        parentId: pid,
        title: g.title || "",
        order: g.order || 0,
        defaultCollapsed: !!g.defaultCollapsed,
        showInRail: g.showInRail !== false
      });
      if(nested && Array.isArray(g.groups)){
        for(var c = 0; c < g.groups.length; c++) emit(g.groups[c], g.id);
      }
    }
    for(i = 0; i < src.length; i++) emit(src[i], null);
    return out;
  }

  function graftSeedGroups(model, seed){
    if(!model) return false;
    var same = seed && model.groups === seed.groups;
    var flat = normalizeGroups(model.groups);
    var grafted = false;
    if(seed && !same){
      var seedFlat = normalizeGroups(seed.groups);
      var have = {};
      flat.forEach(function(g){ if(g.id) have[g.id] = 1; });
      seedFlat.forEach(function(g){
        if(g.id && !have[g.id]){
          flat.push(g);
          have[g.id] = 1;
          grafted = true;
        }
      });
    }
    model.groups = flat;
    return grafted;
  }

  function mintSlugCardIds(model){
    var changed = false;
    (model.cards || []).forEach(function(c){
      if(!c) return;
      if(!isUuid(c.id)){
        c.id = mintUuid();
        changed = true;
      }
    });
    return changed;
  }

  function groupParentId(g){ return g && g.parentId ? g.parentId : null; }

  function knownGroupIds(groups){
    var ids = {};
    (groups || []).forEach(function(g){ if(g && g.id) ids[g.id] = 1; });
    return ids;
  }

  function rootGroups(groups){
    var ids = knownGroupIds(groups);
    return byOrder((groups || []).filter(function(g){
      var p = groupParentId(g);
      return g && (!p || !ids[p]);
    }));
  }

  function childGroupsOf(groups, id){
    return byOrder((groups || []).filter(function(g){ return g && g.parentId === id; }));
  }

  function idsUnderGroup(groups, id){
    var ids = {};
    ids[id] = 1;
    var grew = true;
    while(grew){
      grew = false;
      (groups || []).forEach(function(g){
        if(g && g.parentId && ids[g.parentId] && !ids[g.id]){
          ids[g.id] = 1;
          grew = true;
        }
      });
    }
    return ids;
  }

  function groupChoices(){
    var model = window.__ADC.model;
    var out = [];
    function walk(list, prefix){
      list.forEach(function(g){
        var label = prefix ? prefix + " / " + (g.title || g.id) : (g.title || g.id);
        out.push({ value: g.id, label: label });
        walk(childGroupsOf(model.groups, g.id), label);
      });
    }
    walk(rootGroups(model.groups), "");
    return out;
  }

  /* One collapsed-tray icon per checked group, parents before their subgroups. */
  function renderRail(){
    var host = document.querySelector(".rail-icos");
    var model = window.__ADC && window.__ADC.model;
    if(!host || !model) return;
    host.textContent = "";
    function paint(group){
      if(!group) return;
      if(group.showInRail !== false){
      var cards = byOrder((model.cards || []).filter(function(c){ return c && c.groupId === group.id; }));
      var first = cards.length ? cards[0] : null;
      var mark = first && first.icon ? String(first.icon) : "";
      if(!mark){
        var title = String(group.title || "").replace(/^\s+/, "");
        mark = title ? title.charAt(0) : "";
      }
      var b = document.createElement("button");
      b.type = "button";
      b.className = "rail-ico";
      var label = group.title || group.id || "Group";
      b.title = label;
      b.setAttribute("aria-label", label);
      if(first && (first.app || first.name)) b.setAttribute("data-rail-app", first.app || first.name);
      b.textContent = mark;
      host.appendChild(b);
      }
      childGroupsOf(model.groups, group.id).forEach(paint);
    }
    rootGroups(model.groups).forEach(paint);
  }

  /* 2026-10-09: optional card.iconImage. A relative path under the page folder (assets/icons/chrome.png)
     or an http(s)/data URL. Shown as an image in place of the emoji; card.icon stays the fallback
     when iconImage is empty or the image does not load. Windows paths (C:\...) are not served. */
  function cardIconSrc(card){
    var raw = String((card && card.iconImage) || "").replace(/^\s+|\s+$/g, "");
    if(!raw) return "";
    if(/^(https?:|data:image\/)/i.test(raw)) return raw;
    if(/^[a-z]:[\\\/]|^\\\\|^file:|^[a-z][a-z0-9+.-]*:/i.test(raw)) return "";
    return raw.replace(/\\/g, "/").replace(/^\.?\//, "");
  }
  /* adc-icontile (2026-10-11): every card icon sits on a small tile so dark logos read on the dark theme.
     card.iconTile is exactly "light", "dark" or "none"; missing or anything else = light (light is never stored). */
  function cardIconTile(card){
    var v = String((card && card.iconTile) || "").replace(/^\s+|\s+$/g, "").toLowerCase();
    return (v === "dark" || v === "none") ? v : "light";
  }
  function paintIconTile(el, card){
    el.classList.remove("adc-tile-light", "adc-tile-dark");
    var t = cardIconTile(card);
    if(t !== "none") el.classList.add("adc-tile-" + t);
    el.setAttribute("data-icon-tile", t);
  }
  window.__adcCardIconTile = cardIconTile;
  function setCardIcon(el, card){
    paintIconTile(el, card);
    el.textContent = (card && card.icon) || "";
    var src = cardIconSrc(card);
    if(!src) return;
    var img = document.createElement("img");
    img.className = "adc-ico-img";
    img.alt = "";
    img.draggable = false;
    img.addEventListener("error", function(){
      if(img.parentNode === el){ el.classList.remove("adc-has-img"); el.textContent = (card && card.icon) || ""; }
    });
    img.src = src;
    el.textContent = "";
    el.classList.add("adc-has-img");
    el.appendChild(img);
  }
  window.__adcCardIconSrc = cardIconSrc;

  /* 2026-10-11 (adc-iconpick): picture buttons for the Icon row of the Edit card form (adc-editux).
     Emoji grid, Image file... (host POST /adc-icon saves a PNG in the Data folder's data/icons, next to
     the site icons), Use site icon (existing GET /adc-favicon), Remove picture, Reset. The form's own
     preview, hint, error line, dirty check and Save are reused; this only adds the buttons and keeps
     the iconImage value in a hidden input that the form's snapshot sees. */
  var ICON_EMOJI = ["\uD83D\uDCD2","\uD83D\uDCB0","\uD83D\uDCB3","\uD83C\uDFE6","\uD83D\uDCC8","\uD83D\uDCC9","\uD83D\uDCB5","\uD83E\uDDFE",
    "\uD83D\uDCCA","\uD83D\uDCC5","\uD83D\uDCE7","\uD83D\uDCAC","\uD83D\uDCDE","\uD83C\uDF10","\uD83D\uDD0D","\uD83D\uDCC1",
    "\uD83D\uDCC4","\uD83D\uDCDD","\uD83D\uDDC2","\uD83D\uDCCC","\uD83D\uDD12","\uD83D\uDD11","\u2699","\uD83D\uDEE0",
    "\uD83D\uDCBB","\uD83D\uDDA5","\uD83D\uDCF1","\uD83E\uDD16","\uD83D\uDE80","\u2601","\uD83D\uDDC4","\uD83D\uDC19",
    "\uD83C\uDFA8","\uD83D\uDCD0","\uD83E\uDDF0","\uD83C\uDFAC","\uD83C\uDFB5","\uD83D\uDCF7","\uD83D\uDED2","\uD83D\uDE9A",
    "\uD83C\uDFE0","\u2708","\uD83D\uDE97","\u2764","\u2B50","\u2705","\u26A0","\uD83D\uDC34"];
  var ICON_UPLOAD_EXT = ["png", "jpg", "jpeg", "ico", "webp", "gif"]; /* SVG refused: it can carry script on the cockpit's own origin */
  var ICON_UPLOAD_MAX = 1024 * 1024;
  /* Origin of an http(s) card URL ("https://host[:port]"), or "" (no site icon for exe/app/other cards). */
  function iconSiteOrigin(url){
    var m = /^(https?):\/\/([^\/?#@\s]+@)?([^\/?#:\s]+)(:\d+)?(?:[\/?#]|$)/i.exec(String(url || "").trim());
    if(!m) return "";
    return m[1].toLowerCase() + "://" + m[3].toLowerCase() + (m[4] && !((m[1].toLowerCase() === "https" && m[4] === ":443") || (m[1].toLowerCase() === "http" && m[4] === ":80")) ? m[4] : "");
  }
  /* GET path for the host's favicon cache, or "" when the card is not a web card. */
  function iconSiteRequest(url){
    var o = iconSiteOrigin(url);
    return o ? "/adc-favicon?url=" + encodeURIComponent(String(url).trim()) : "";
  }
  /* Client-side check before the upload; the host checks again. Returns "" or a message. */
  function iconUploadProblem(name, size){
    var ext = (/\.([a-z0-9]+)$/i.exec(String(name || "")) || [])[1];
    ext = ext ? ext.toLowerCase() : "";
    if(ICON_UPLOAD_EXT.indexOf(ext) < 0) return "Use a PNG, JPG, ICO, WebP or GIF file" + (ext === "svg" ? " (SVG is not accepted)." : ".");
    if(!(size > 0)) return "That file is empty.";
    if(size > ICON_UPLOAD_MAX) return "That image is over 1 MB.";
    return "";
  }
  /* iconImage on the saved card: changed only when the picker changed it (so a site icon filled in by
     ensureFavicons while the form was open is kept); "" removes it. */
  function applyIconImage(target, start, now){
    var v = String(now || "").trim();
    if(v === String(start || "").trim()) return target;
    if(v) target.iconImage = v;
    else delete target.iconImage;
    return target;
  }
  /* Builds the picker for the form's Icon cell. o = { card, input (the form's emoji box), getUrl(), changed() }. */
  function iconPicker(o){
    var start = String((o.card && o.card.iconImage) || "");
    var startIcon = o.input.value;
    var busy = false;
    function mk(tag, cls, text){ var n = document.createElement(tag); if(cls) n.className = cls; if(text != null) n.textContent = text; return n; }
    function btn(label, act, title){ var b = mk("button", "adc-cf-iconbtn", label); b.type = "button"; b.setAttribute("data-act", act); if(title) b.title = title; return b; }
    var hidden = mk("input"); hidden.type = "hidden"; hidden.value = start; hidden.className = "adc-cf-iconimage";
    var bar = mk("div", "adc-cf-iconbar");
    var bEmoji = btn("Emoji \u25BE", "emoji", "Pick an emoji");
    bEmoji.setAttribute("aria-haspopup", "true"); bEmoji.setAttribute("aria-expanded", "false"); bEmoji.setAttribute("aria-controls", "adcCfEmojiGrid");
    var bFile = btn("Image file\u2026", "file", "PNG, JPG, ICO, WebP or GIF, up to 1 MB. Saved in data/icons.");
    var bSite = btn("Use site icon", "site", "");
    var bRemove = btn("Remove picture", "remove", "Show the emoji instead of the picture");
    var bReset = btn("Reset", "reset", "Back to the icon and picture this card had when the form opened");
    [bEmoji, bFile, bSite, bRemove, bReset].forEach(function(b){ bar.appendChild(b); });
    var grid = mk("div", "adc-cf-emojigrid"); grid.id = "adcCfEmojiGrid"; grid.hidden = true;
    grid.setAttribute("role", "group"); grid.setAttribute("aria-label", "Emoji");
    var msg = mk("div", "adc-cf-hint adc-cf-iconmsg"); msg.setAttribute("role", "status"); msg.setAttribute("aria-live", "polite"); msg.hidden = true;
    var file = mk("input", "adc-cf-iconfile"); file.type = "file"; file.hidden = true; file.tabIndex = -1;
    file.accept = ".png,.jpg,.jpeg,.ico,.webp,.gif,image/png,image/jpeg,image/x-icon,image/vnd.microsoft.icon,image/webp,image/gif";
    bar.appendChild(file);
    function say(text, err){ msg.textContent = text || ""; msg.className = (err ? "adc-cf-err" : "adc-cf-hint") + " adc-cf-iconmsg"; msg.hidden = !text; }
    function openGrid(on){
      grid.hidden = !on; bEmoji.setAttribute("aria-expanded", on ? "true" : "false");
      if(on){ var f = grid.querySelector("button"); if(f) try { f.focus(); } catch(e){} }
    }
    function pickEmoji(e){ o.input.value = e; openGrid(false); try { o.input.focus(); } catch(_){} say(""); o.changed(); }
    ICON_EMOJI.concat([""]).forEach(function(e){
      var g = mk("button", "adc-cf-emojicell" + (e ? "" : " adc-cf-emojinone"), e || "None");
      g.type = "button"; g.setAttribute("data-emoji", e); g.title = e ? e : "No emoji";
      g.addEventListener("click", function(){ pickEmoji(e); });
      grid.appendChild(g);
    });
    function setImage(v){ hidden.value = String(v || "").trim(); o.changed(); }
    function setBusy(on){ busy = !!on; paint(); }
    function fail(e){ say(e && e.message ? e.message : String(e), true); }
    bEmoji.addEventListener("click", function(){ openGrid(grid.hidden); });
    bRemove.addEventListener("click", function(){ say(""); setImage(""); });
    bReset.addEventListener("click", function(){ o.input.value = startIcon; hidden.value = start; openGrid(false); say(""); o.changed(); });
    bFile.addEventListener("click", function(){ file.value = ""; file.click(); });
    file.addEventListener("change", function(){
      var f = file.files && file.files[0]; if(!f) return;
      var bad = iconUploadProblem(f.name, f.size); if(bad){ say(bad, true); return; }
      var rd = new FileReader();
      rd.onerror = function(){ say("Could not read that file.", true); };
      rd.onload = function(){
        var data = String(rd.result || "").replace(/^data:[^,]*,/, "");
        setBusy(true); say("Saving the picture\u2026");
        fetch("/adc-icon", { method: "POST", cache: "no-store", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: f.name, data: data }) })
          .then(function(r){
            if(r.status === 404 || r.status === 405) throw new Error("This cockpit build cannot save image files yet.");
            return r.json().catch(function(){ return {}; }).then(function(j){ if(!r.ok || !j.iconImage) throw new Error(j.message || "The picture was not saved."); return j; });
          })
          .then(function(j){ busy = false; say((j.reused ? "Using the existing copy " : "Saved ") + j.iconImage + ". Press Save to put it on the card."); setImage(j.iconImage); }, function(e){ busy = false; fail(e); paint(); });
      };
      rd.readAsDataURL(f);
    });
    bSite.addEventListener("click", function(){
      var url = o.getUrl(), q = iconSiteRequest(url);
      if(!q){ say("Site icon is only for web cards (http or https URL).", true); return; }
      setBusy(true); say("Looking for the icon of " + iconSiteOrigin(url) + "\u2026");
      fetch(q, { cache: "no-store" }).then(function(r){
        if(r.status === 404 || r.status === 405) throw new Error("This cockpit build cannot fetch site icons.");
        if(r.status !== 200) throw new Error("No site icon found for " + iconSiteOrigin(url) + "; the current icon stays.");
        return r.json();
      }).then(function(j){
        if(!j || !j.iconImage) throw new Error("No site icon found for " + iconSiteOrigin(url) + "; the current icon stays.");
        busy = false; say("Site icon saved: " + j.iconImage + ". Press Save to put it on the card."); setImage(j.iconImage);
      }).catch(function(e){ busy = false; fail(e); paint(); });
    });
    function paint(){
      var origin = iconSiteOrigin(o.getUrl());
      bFile.disabled = busy;
      bSite.disabled = busy || !origin;
      bSite.title = origin ? "Fetch the icon of " + origin + " and save it locally" : "Only for web cards (http or https URL)";
      bRemove.disabled = busy || !hidden.value;
      bReset.disabled = busy || (hidden.value === start && o.input.value === startIcon);
    }
    paint();
    return {
      hidden: hidden, bar: bar, grid: grid, msg: msg, file: file, paint: paint,
      image: function(){ return hidden.value; },
      busy: function(){ return busy; },
      removed: function(){ return !!start && !hidden.value; },
      /* Esc: closes the emoji grid first (returns true), so the form's Esc does not discard or close. */
      closeGrid: function(){ if(grid.hidden) return false; openGrid(false); try { bEmoji.focus(); } catch(e){} return true; },
      apply: function(target){ return applyIconImage(target, start, hidden.value); }
    };
  }
  window.__adcIconPick = { picker: iconPicker, origin: iconSiteOrigin, request: iconSiteRequest, check: iconUploadProblem, apply: applyIconImage, emoji: ICON_EMOJI };

  /* 2026-10-09: web cards get their site icon by default. For a card with an http(s) url and no
     iconImage, ask the host (GET /adc-favicon) to fetch and cache the icon locally, then save the
     returned path into the card. A hand-set iconImage is never replaced. If the host has no such
     endpoint (HTML cockpit, older WV2 build) this switches itself off. If no icon is found, the
     emoji stays. */
  var favState = { off: false, tried: {}, running: false };
  function ensureFavicons(){
    if(favState.off || favState.running) return;
    var m = window.__ADC && window.__ADC.model;
    if(!m || !Array.isArray(m.cards)) return;
    var todo = m.cards.filter(function(c){
      return c && c.id && !c.iconImage && /^https?:\/\//i.test(String(c.url || "").trim()) && !favState.tried[c.id];
    });
    if(!todo.length) return;
    favState.running = true;
    var got = 0;
    function finish(tries){
      if(got && typeof liveBusy === "function" && liveBusy() && tries < 60){
        setTimeout(function(){ finish(tries + 1); }, 1000);
        return;
      }
      favState.running = false;
      if(got) refreshTrayQuiet();
    }
    (function next(i){
      if(i >= todo.length || favState.off){ finish(0); return; }
      var c = todo[i];
      favState.tried[c.id] = 1;
      fetch("/adc-favicon?url=" + encodeURIComponent(String(c.url).trim()), { cache: "no-store" }).then(function(r){
        if(r.status === 404 || r.status === 405){ favState.off = true; return null; }
        return r.status === 200 ? r.json() : null;
      }).then(function(j){
        if(!j || !j.iconImage) return;
        var cur = window.__ADC && window.__ADC.model;
        var live = cur && findById(cur.cards, c.id);
        if(live && !live.iconImage){ live.iconImage = String(j.iconImage); got++; }
      }, function(){}).then(function(){ next(i + 1); });
    })(0);
  }
  window.__adcEnsureFavicons = ensureFavicons;
  setInterval(ensureFavicons, 60000);

  /* 2026-10-09: drag a group header to reorder groups. Top-level groups move among top-level groups;
     a subgroup moves only among its parent's subgroups. The new order is written to every sibling's
     order field and saved like any other edit (groups.json via /adc-data). */
  function groupSiblings(model, group){
    var pid = group && group.parentId ? group.parentId : null;
    var ids = knownGroupIds(model.groups);
    if(pid && ids[pid]) return childGroupsOf(model.groups, pid);
    return rootGroups(model.groups);
  }
  function reorderGroup(dragId, targetId, after){
    var model = window.__ADC && window.__ADC.model;
    if(!model || !dragId || !targetId || dragId === targetId) return false;
    var drag = findById(model.groups, dragId);
    var target = findById(model.groups, targetId);
    if(!drag || !target) return false;
    var sibs = groupSiblings(model, drag);
    if(sibs.indexOf(target) < 0) return false;
    var list = sibs.filter(function(g){ return g !== drag; });
    var at = list.indexOf(target) + (after ? 1 : 0);
    list.splice(at, 0, drag);
    var changed = false;
    for(var i = 0; i < list.length; i++){
      if(list[i].order !== i + 1){ list[i].order = i + 1; changed = true; }
    }
    return changed;
  }
  window.__adcReorderGroup = reorderGroup;
  function clearGroupInsert(){
    document.querySelectorAll(".adc-group-insert-before, .adc-group-insert-after").forEach(function(el){
      el.classList.remove("adc-group-insert-before");
      el.classList.remove("adc-group-insert-after");
    });
  }
  function bindGroupReorder(){
    var root = document.querySelector(".lp-tray-scroll");
    if(!root || root.dataset.adcGroupReorder) return;
    root.dataset.adcGroupReorder = "1";
    function headerFrom(node){
      if(node && node.nodeType !== 1) node = node.parentElement;
      var sum = node && node.closest ? node.closest("summary") : null;
      if(!sum || !root.contains(sum)) return null;
      var d = sum.parentElement;
      return d && d.matches && d.matches("details.lp-group") ? d : null;
    }
    /* The group at the dragged group's level under the pointer: walk up to a sibling element. */
    function siblingFrom(node, dragEl){
      if(node && node.nodeType !== 1) node = node.parentElement;
      var d = node && node.closest ? node.closest("details.lp-group") : null;
      while(d && d.parentElement !== dragEl.parentElement){
        d = d.parentElement && d.parentElement.closest ? d.parentElement.closest("details.lp-group") : null;
      }
      return d;
    }
    function dragEl(){
      var g = window.__adcGroupDrag;
      return g ? root.querySelector('details.lp-group[data-group-id="' + (window.CSS && CSS.escape ? CSS.escape(g.id) : g.id) + '"]') : null;
    }
    /* Ends a group drag. Also called on drop: a successful drop redraws the tray, so the dragged
       header is gone from the page and its dragend never reaches the tray. */
    function endGroupDrag(){
      clearGroupInsert();
      root.querySelectorAll(".adc-group-dragging").forEach(function(el){ el.classList.remove("adc-group-dragging"); });
      document.body.classList.remove("adc-group-reordering");
      window.__adcGroupDrag = null;
    }
    document.addEventListener("dragend", function(){ if(window.__adcGroupDrag) endGroupDrag(); }, true);
    root.addEventListener("dragstart", function(e){
      if(window.__adcGroupDrag) endGroupDrag();
      if(window.__adcTrayDrag || window.__adcTabDrag) return;
      var d = headerFrom(e.target);
      if(!d) return;
      e.stopPropagation();
      window.__adcGroupDrag = { id: d.getAttribute("data-group-id") };
      d.classList.add("adc-group-dragging");
      document.body.classList.add("adc-group-reordering");
      try {
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", "adc-group:" + window.__adcGroupDrag.id);
      } catch(_){}
    });
    root.addEventListener("dragover", function(e){
      if(!window.__adcGroupDrag) return;
      var me = dragEl();
      if(!me) return;
      var t = siblingFrom(e.target, me);
      if(!t){ clearGroupInsert(); return; }
      e.preventDefault();
      if(e.dataTransfer) e.dataTransfer.dropEffect = "move";
      if(t === me){ clearGroupInsert(); return; }
      var r = t.getBoundingClientRect();
      var after = e.clientY > r.top + r.height / 2;
      var cls = after ? "adc-group-insert-after" : "adc-group-insert-before";
      if(!t.classList.contains(cls)){
        clearGroupInsert();
        t.classList.add(cls);
      }
    });
    root.addEventListener("drop", function(e){
      if(!window.__adcGroupDrag) return;
      e.preventDefault();
      e.stopPropagation();
      var me = dragEl();
      var t = me ? siblingFrom(e.target, me) : null;
      var id = window.__adcGroupDrag.id;
      endGroupDrag();
      if(!t || t === me) return;
      var r = t.getBoundingClientRect();
      var after = e.clientY > r.top + r.height / 2;
      if(reorderGroup(id, t.getAttribute("data-group-id"), after)){
        refreshTrayQuiet();
        var g = findById(window.__ADC.model.groups, id);
        var lp = window.__ADC.lp;
        if(lp && typeof lp.toast === "function") lp.toast("Moved group " + ((g && g.title) || id));
      }
    });
    root.addEventListener("dragend", function(){
      if(window.__adcGroupDrag) endGroupDrag();
    });
  }

  /* Builds one tray tile with the attributes drag and click already expect. */
  function makeTile(card){
    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "lp-tile" + (card.color ? " " + card.color : "");
    btn.draggable = true;
    btn.setAttribute("data-app", card.app || card.name || "");
    btn.setAttribute("data-card-id", card.id || "");
    if(card.icon) btn.setAttribute("data-ico", card.icon);
    if(card.url) btn.setAttribute("data-url", card.url);
    var ico = document.createElement("span");
    ico.className = "ico";
    setCardIcon(ico, card);
    var name = document.createElement("span");
    name.className = "name";
    name.textContent = card.name || "";
    var desc = document.createElement("span");
    desc.className = "desc";
    desc.textContent = card.description || "";
    btn.appendChild(ico);
    btn.appendChild(name);
    btn.appendChild(desc);
    return btn;
  }

  /* Draws groups and tiles. Groups start open so collapse memory can close them. */
  function renderTray(){
    var root = document.querySelector(".lp-tray-scroll");
    if(!root || !window.__ADC || !window.__ADC.model) return;
    var model = window.__ADC.model;
    root.textContent = "";
    function appendGroup(host, group){
      var details = document.createElement("details");
      details.className = "lp-group";
      details.open = true;
      details.setAttribute("data-group-id", group.id);
      if(group.defaultCollapsed) details.dataset.defaultCollapsed = "true";
      var summary = document.createElement("summary");
      var chev = document.createElement("span");
      chev.className = "chev";
      summary.appendChild(chev);
      var rows = byOrder(model.cards.filter(function(c){ return c.groupId === group.id; }));
      if(window.__adcGroupTitle) window.__adcGroupTitle(summary, group); /* adc-grename */
      else summary.appendChild(document.createTextNode(" " + group.title + " "));
      summary.draggable = true;
      summary.title = "Drag to reorder groups";
      var count = document.createElement("span");
      count.className = "count";
      count.textContent = String(rows.length);
      summary.appendChild(count);
      if(window.__adcGroupTabButton) window.__adcGroupTabButton(summary, group);
      details.appendChild(summary);
      var body = document.createElement("div");
      body.className = "lp-body";
      rows.forEach(function(card){ body.appendChild(makeTile(card)); });
      details.appendChild(body);
      var kids = childGroupsOf(model.groups, group.id);
      if(kids.length){
        var nest = document.createElement("div");
        nest.className = "lp-subgroups";
        kids.forEach(function(child){ appendGroup(nest, child); });
        details.appendChild(nest);
      }
      host.appendChild(details);
    }
    rootGroups(model.groups).forEach(function(group){ appendGroup(root, group); });
  }

  /* Restores and remembers collapsed groups. The key is the title, not the count. */
  function bindGroupCollapse(){
    var KEY = "lp-group-collapsed";
    function load(){ try { return JSON.parse(localStorage.getItem(KEY) || "{}") || {}; } catch(e){ return {}; } }
    function save(m){ try { localStorage.setItem(KEY, JSON.stringify(m)); } catch(e){} }
    function keyOf(d){
      var s = d.querySelector(":scope>summary");
      if(!s) return null;
      var c = s.cloneNode(true);
      var x = c.querySelector(".count");
      if(x) x.remove();
      c.querySelectorAll(".adc-gtab-open, input.adc-grename").forEach(function(el){ el.remove(); });
      return c.textContent.trim();
    }
    var state = load();
    document.querySelectorAll("details.lp-group").forEach(function(d){
      if(d.dataset.adcCollapseBound) return;
      d.dataset.adcCollapseBound = "1";
      var k = keyOf(d);
      if(!k) return;
      if(Object.prototype.hasOwnProperty.call(state, k)) d.open = !state[k];
      else if(d.dataset.defaultCollapsed === "true") d.open = false;
      d.addEventListener("toggle", function(){
        var m = load();
        m[k] = !d.open;
        save(m);
      });
    });
  }

  /* Restores the last tab unless a config query is already doing that. */
  function restoreActiveTab(){
    try {
      var savedTab = /[?&]config=/.test(location.search || "") ? null : localStorage.getItem("lp-active-tab");
      if(savedTab && document.querySelector('.tab[data-view="' + savedTab + '"]') && typeof window.__activateView === "function"){
        window.__activateView(savedTab);
      }
    } catch(e){}
  }


  /* One width for all tray cards: the widest card's content, applied to every card in every group.
     Clear inline widths first so the measure is max-content, not the previous equal width.
     The tray keeps width: var(--lp-tray-width) and min-width: 0. Wide cards scroll in the tray. */
  function equalizeTrayCards(force){
    var tray = document.querySelector(".lp-tray");
    if(!tray || tray.classList.contains("adc-eq-busy")) return;
    var tw = Math.round(tray.getBoundingClientRect().width);
    if(!force && tray.dataset.adcEqW === String(tw)) return;
    tray.classList.add("adc-eq-busy");
    try {
      /* 2026-10-09: one width for every tray card in every group (was one width per group, so a group
         with a single short card, e.g. Banking's Joint / reserves, drew a narrow card). Cards in a
         collapsed group are not laid out and measure 0; they still get the shared width, and opening
         a group re-runs this (see bindCardWidths). */
      var tiles = tray.querySelectorAll("details.lp-group > .lp-body > .lp-tile");
      var i;
      for(i = 0; i < tiles.length; i++){
        tiles[i].style.width = "";
        tiles[i].style.minWidth = "";
        tiles[i].style.maxWidth = "";
        tiles[i].style.flexBasis = "";
      }
      if(tiles.length){
        /* Measure the title (icon + name) only; the description wraps inside the shared width. */
        tray.classList.add("adc-eq-measure");
        void tray.offsetWidth;
        var max = 0;
        for(i = 0; i < tiles.length; i++){
          var w = tiles[i].getBoundingClientRect().width;
          if(w > max) max = w;
        }
        tray.classList.remove("adc-eq-measure");
        if(max > 0){
          var px = Math.ceil(max) + "px";
          for(i = 0; i < tiles.length; i++){
            tiles[i].style.width = px;
            tiles[i].style.minWidth = px;
            tiles[i].style.maxWidth = px;
            tiles[i].style.flexBasis = px;
          }
          tray.dataset.adcEqPx = px;
          /* Same height inside each group, so a longer description does not make one card stand out. */
          for(i = 0; i < tiles.length; i++) tiles[i].style.minHeight = "";
          void tray.offsetWidth;
          var bodies = tray.querySelectorAll("details.lp-group > .lp-body");
          for(var bI = 0; bI < bodies.length; bI++){
            var row = bodies[bI].querySelectorAll(":scope > .lp-tile");
            var tall = 0;
            for(i = 0; i < row.length; i++){ var hh = row[i].getBoundingClientRect().height; if(hh > tall) tall = hh; }
            if(tall > 0) for(i = 0; i < row.length; i++) row[i].style.minHeight = Math.ceil(tall) + "px";
          }
        }
      }
      tray.dataset.adcEqW = String(Math.round(tray.getBoundingClientRect().width));
    } finally {
      tray.classList.remove("adc-eq-measure");
      tray.classList.remove("adc-eq-busy");
    }
  }
  window.__adcEqualizeTrayCards = equalizeTrayCards;

  /* Remeasure when the tray or the window changes size, including the splitter. */
  function bindCardWidths(){
    var tray = document.querySelector(".lp-tray");
    if(!tray || tray.dataset.adcEqBound) return;
    tray.dataset.adcEqBound = "1";
    var pending = 0;
    var wantForce = false;
    function schedule(force){
      if(force) wantForce = true;
      if(pending) cancelAnimationFrame(pending);
      pending = requestAnimationFrame(function(){
        pending = 0;
        var runForce = wantForce;
        wantForce = false;
        equalizeTrayCards(runForce);
      });
    }
    if(window.ResizeObserver){
      var ro = new ResizeObserver(function(){ schedule(false); });
      ro.observe(tray);
    } else {
      window.addEventListener("resize", function(){ schedule(false); });
    }
    if(document.fonts && document.fonts.ready){
      document.fonts.ready.then(function(){ equalizeTrayCards(true); });
    }
    // A group that was collapsed had cards that measured 0. Remeasure when any group opens.
    tray.addEventListener("toggle", function(e){
      var t = e.target;
      if(t && t.matches && t.matches("details.lp-group") && t.open) schedule(true);
    }, true);
    schedule(true);
  }

  function dropBeforeCardId(body, x, y, skipId){
    var tiles = body.querySelectorAll(":scope > .lp-tile");
    for(var i = 0; i < tiles.length; i++){
      var id = tiles[i].getAttribute("data-card-id") || "";
      if(!id || id === skipId) continue;
      var r = tiles[i].getBoundingClientRect();
      if(r.width < 1 || r.height < 1) continue;
      var sameBand = y >= r.top - 6 && y <= r.bottom + 6;
      if(y < r.top || (sameBand && x < r.left + r.width / 2)) return id;
    }
    return "";
  }

  function clearGroupDrop(){
    document.querySelectorAll(".adc-group-drop").forEach(function(el){ el.classList.remove("adc-group-drop"); });
  }

  /* Redraws the tray without rebuilding stage panes, then remeasures card widths. */
  function refreshTrayQuiet(){
    saveOverlay();
    renderTray();
    renderRail();
    var lp = window.__ADC.lp;
    if(lp){
      if(lp.refreshIcons) lp.refreshIcons();
      if(lp.bindTrayTiles) lp.bindTrayTiles();
      if(lp.bindRail) lp.bindRail();
    }
    bindGroupCollapse();
    if(editing){
      clearEditControls();
      mountEditControls();
    }
    equalizeTrayCards(true);
  }

  /* Moves a tray card into a group and orders it from the drop point. Saves adc-page-data only. */
  function moveTrayCard(cardId, groupId, x, y){
    var model = window.__ADC && window.__ADC.model;
    if(!model || !cardId || !groupId) return;
    var card = findById(model.cards, cardId);
    var group = findById(model.groups, groupId);
    if(!card || !group) return;
    var fromGroup = card.groupId;
    var groupEl = null;
    var groups = document.querySelectorAll("details.lp-group");
    for(var g = 0; g < groups.length; g++){
      if(groups[g].getAttribute("data-group-id") === groupId){ groupEl = groups[g]; break; }
    }
    var body = groupEl ? groupEl.querySelector(":scope > .lp-body") : null;
    var beforeId = body ? dropBeforeCardId(body, x, y, card.id) : "";
    card.groupId = groupId;
    var dest = byOrder(model.cards.filter(function(c){ return c.groupId === groupId && c.id !== card.id; }));
    var at = dest.length;
    if(beforeId){
      for(var i = 0; i < dest.length; i++){
        if(dest[i].id === beforeId){ at = i; break; }
      }
    }
    dest.splice(at, 0, card);
    for(var j = 0; j < dest.length; j++) dest[j].order = j + 1;
    if(fromGroup && fromGroup !== groupId){
      var src = byOrder(model.cards.filter(function(c){ return c.groupId === fromGroup; }));
      for(var k = 0; k < src.length; k++) src[k].order = k + 1;
    }
    if(groupEl && !groupEl.open) groupEl.open = true;
    window.__adcTrayDrag = null;
    document.body.classList.remove("adc-dragging");
    clearGroupDrop();
    refreshTrayQuiet();
  }

  /* Highlight a group during a tray-card drag. Drop moves the card. Pane drops are outside this root. */
  function bindGroupCardDnD(){
    var root = document.querySelector(".lp-tray-scroll");
    if(!root || root.dataset.adcGroupDrop) return;
    root.dataset.adcGroupDrop = "1";
    function groupFrom(e){
      var node = e.target;
      if(!node) return null;
      if(node.nodeType !== 1) node = node.parentElement;
      if(!node || !node.closest) return null;
      var group = node.closest("details.lp-group");
      if(!group || !root.contains(group)) return null;
      return group;
    }
    root.addEventListener("dragover", function(e){
      if(!window.__adcTrayDrag || window.__adcTabDrag) return;
      var group = groupFrom(e);
      if(!group) return;
      e.preventDefault();
      if(e.dataTransfer) e.dataTransfer.dropEffect = "move";
      if(!group.classList.contains("adc-group-drop")){
        clearGroupDrop();
        group.classList.add("adc-group-drop");
      }
    });
    root.addEventListener("dragleave", function(e){
      var group = groupFrom(e);
      if(!group) return;
      var rel = e.relatedTarget;
      if(rel && group.contains(rel)) return;
      group.classList.remove("adc-group-drop");
    });
    root.addEventListener("drop", function(e){
      if(!window.__adcTrayDrag || window.__adcTabDrag) return;
      var group = groupFrom(e);
      if(!group) return;
      e.preventDefault();
      e.stopPropagation();
      var info = window.__adcTrayDrag;
      clearGroupDrop();
      moveTrayCard(info && info.cardId, group.getAttribute("data-group-id"), e.clientX, e.clientY);
    });
  }

  /* One click nests a group under this one and opens the editor. No parent picker.
     The new group's parentId is this group's id. Order is among its siblings. */
  function addSubgroup(parent){
    if(!parent || !parent.id || !window.__ADC || !window.__ADC.model) return;
    var parentId = parent.id;
    var model = window.__ADC.model;
    var order = 1;
    model.groups.forEach(function(g){
      if(g && g.parentId === parentId){
        var n = parseInt(g.order, 10);
        if(isFinite(n) && n >= order) order = n + 1;
      }
    });
    editGroup({
      title: "New group",
      parentId: parentId,
      order: order,
      defaultCollapsed: false,
      showInRail: true
    }, true);
  }

  /* One click creates the card in this group and opens the editor. No group picker. */
  function addCardInGroup(group){
    if(!group || !window.__ADC || !window.__ADC.model) return;
    var model = window.__ADC.model;
    var order = 1;
    model.cards.forEach(function(c){
      if(c.groupId === group.id){
        var n = parseInt(c.order, 10);
        if(isFinite(n) && n >= order) order = n + 1;
      }
    });
    var card = {
      id: mintUuid(),
      name: "New card",
      app: "",
      description: "",
      icon: "",
      color: "fin",
      groupId: group.id,
      order: order
    };
    model.cards.push(card);
    commit({ tray: true });
    editCard(card, false, null, { lockGroup: true });
  }

  /* Redraws whatever changed and rebinds tiles, tabs, or layout buttons. */
  window.__adcCommit = function(f){ commit(f || {}); }; /* adc-gtabs */
  window.__adcGroupActions = { edit: function(g){ editGroup(g, false); }, addCard: function(g){ addCardInGroup(g); }, addSubgroup: function(g){ addSubgroup(g); } }; /* adc-gmenu */
  /* 2026-10-09 v1.5.0 (adc-setups): edit actions for the right-click menus; edit mode is retired. */
  window.__adcActions = {
    editCard: function(c){ editCard(c, false); },
    deleteCard: function(c){ deleteCard(c); },
    copyCard: function(c){ return copyCard(c); },
    newGroup: function(){ var m = window.__ADC.model; editGroup({ title: "", order: (m.groups || []).length + 1, defaultCollapsed: false }, true); },
    editGroup: function(g){ editGroup(g, false); },
    editLayout: function(l){ editLayout(l, false); },
    addLayout: function(){ var m = window.__ADC.model; editLayout({ id: "", label: "", title: "", order: m.stages.layouts.length + 1, count: 1, cls: "", mini: "", miniCells: 1, cols: [1], rows: [1] }, true); },
    deleteLayout: function(l){ deleteLayout(l); },
    stageDefaults: function(){ editDefaults(); },
    tabList: function(){ openTabList(); },
    editTab: function(t){ editTab(t, false); },
    reloadFromFiles: function(){
      confirmBox("Reload from files", "Reloads cards, groups, setups and layouts from the saved data files and drops this window's page copy of them. Nothing is deleted.", function(){
        try { localStorage.removeItem(OVERLAY_KEY); } catch(e){}
        location.reload();
      });
    },
    commit: function(f){ commit(f || {}); }
  };
  function commit(flags){
    saveOverlay();
    var lp = window.__ADC.lp;
    if(flags.tray){
      renderTray();
      renderRail();
      if(lp){
        lp.refreshIcons();
        lp.bindTrayTiles();
        if(lp.bindRail) lp.bindRail();
        lp.render();
      }
      bindGroupCollapse();
    }
    if(flags.tabs){
      renderTabs();
      if(window.__adcBindViewTabs) window.__adcBindViewTabs();
      if(lp && lp.syncLayoutButtons) lp.syncLayoutButtons();
    }
    if(flags.layouts){
      renderLayoutButtons();
      if(lp){
        lp.applyStageDefs();
        lp.bindLayoutButtons();
        if(lp.syncLayoutButtons) lp.syncLayoutButtons();
        lp.render();
      }
    }
    if(editing){
      clearEditControls();
      mountEditControls();
    }
    if(flags.tray || editing) equalizeTrayCards(true);
  }

  /* Removes every add, edit, and delete control from the page. */
  function clearEditControls(){
    document.querySelectorAll(".adc-mini, .adc-group-bar, .adc-crudbar").forEach(function(n){ n.remove(); });
  }

  /* Edits or creates one card.
     2026-10-11 (adc-editux): friendlier form. Same fields and the same save path as before
     (name, app, description, icon, color, url, groupId, order; iconImage is never touched here, only by the
     adc-iconpick picture buttons, and only when they changed it).
     Adds a labelled grid with hints, a live icon preview, required markers with inline
     messages, URL checks and https:// normalisation, saving/saved/failed feedback with Save
     disabled while saving, a discard check on Cancel/Esc/X/backdrop, Enter (or Ctrl+S) to save. */
  function cardFormGraphemes(s){
    s = String(s || "");
    if(!s) return 0;
    try {
      if(window.Intl && Intl.Segmenter){
        var n = 0;
        for(var seg of new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(s)){ if(seg) n++; }
        return n;
      }
    } catch(e){}
    return Array.from(s).length;
  }
  function cardFormUrl(raw){
    var v = String(raw == null ? "" : raw).replace(/^\s+|\s+$/g, "");
    if(!v) return { value: "", note: "Optional. A web address (https://...) or a full program path (C:\\...\\app.exe)." };
    if(/^https?:\/\//i.test(v)){
      if(/\s/.test(v)) return { error: "A web address cannot contain spaces." };
      if(!/^https?:\/\/[^\/?#\s]+/i.test(v)) return { error: "The web address is incomplete. Example: https://cloud.digitalocean.com/" };
      return { value: v, note: "Web page. Opens in a cockpit pane." };
    }
    if(/^https?:/i.test(v)) return { error: "The web address is incomplete. Example: https://cloud.digitalocean.com/" };
    if(/^[a-z]:\\/i.test(v) || /^\\\\[^\\]/.test(v)){
      return { value: v, note: /\.exe$/i.test(v) ? "Desktop program. Opens embedded in a pane." : "Windows path. A program path should end in .exe." };
    }
    if(!/\s/.test(v) && /^(localhost|[a-z0-9-]+(\.[a-z0-9-]+)+)(:\d+)?([\/?#].*)?$/i.test(v)){
      return { value: "https://" + v, note: "Will be saved as https://" + v };
    }
    if(/^[a-z][a-z0-9+.-]+:/i.test(v) && !/\s/.test(v)) return { value: v, note: "Kept as typed (" + v.split(":")[0] + ": link)." };
    return { error: "Enter a web address (https://...) or a full program path (C:\\...\\app.exe)." };
  }
  function editCard(card, isNew, onDone, opts){
    opts = opts || {};
    var lockGroup = !!opts.lockGroup;
    var lp = window.__ADC && window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    var els = {}, errs = {}, hints = {}, touched = {};
    var form = null, banner = null, confirmBar = null, status = null, saveBtn = null, preview = null, iconNote = null;
    var saving = false, start = "", closed = false;
    var pick = null; /* adc-iconpick: picture buttons for the Icon row */

    function mk(tag, cls, text){
      var n = document.createElement(tag);
      if(cls) n.className = cls;
      if(text != null) n.textContent = text;
      return n;
    }
    function textInput(value, ph){
      var i = mk("input");
      i.type = "text";
      i.value = value == null ? "" : String(value);
      if(ph) i.placeholder = ph;
      i.autocomplete = "off";
      i.spellcheck = false;
      return i;
    }
    function row(parent, key, label, required, input, hint){
      var id = "adcCf_" + key;
      var lab = mk("label", "adc-cf-label", label);
      lab.htmlFor = id;
      if(required){
        var star = mk("span", "adc-cf-req", " *");
        star.title = "Required";
        star.setAttribute("aria-hidden", "true");
        lab.appendChild(star);
        input.setAttribute("aria-required", "true");
        input.required = true;
      }
      input.id = id;
      var cell = mk("div", "adc-cf-cell");
      cell.appendChild(input);
      var h = mk("div", "adc-cf-hint", hint || "");
      h.id = id + "_hint";
      var e = mk("div", "adc-cf-err");
      e.id = id + "_err";
      e.hidden = true;
      cell.appendChild(h);
      cell.appendChild(e);
      input.setAttribute("aria-describedby", h.id + " " + e.id);
      parent.appendChild(lab);
      parent.appendChild(cell);
      els[key] = input; errs[key] = e; hints[key] = h;
      return cell;
    }
    function snapshot(){
      var o = {};
      Object.keys(els).forEach(function(k){ o[k] = els[k].value; });
      return JSON.stringify(o);
    }
    function dirty(){ return !!form && snapshot() !== start; }

    /* Field checks. Each returns an error message or "". */
    function check(key){
      var v = els[key] ? els[key].value : "";
      if(key === "name") return v.replace(/^\s+|\s+$/g, "") ? "" : "Title is required. It is the name shown on the card.";
      if(key === "icon"){
        var n = cardFormGraphemes(v.replace(/^\s+|\s+$/g, ""));
        return n > 1 ? "Use just one emoji (this has " + n + " characters)." : "";
      }
      if(key === "url"){ var u = cardFormUrl(v); return u.error || ""; }
      if(key === "group") return v ? "" : "Pick a group for the card.";
      if(key === "order") return /^\s*\d+\s*$/.test(v) && Number(v) >= 1 ? "" : "Order must be a whole number, 1 or more.";
      return "";
    }
    function showErr(key, msg){
      if(!errs[key]) return;
      errs[key].textContent = msg || "";
      errs[key].hidden = !msg;
      els[key].classList.toggle("adc-cf-invalid", !!msg);
      if(msg) els[key].setAttribute("aria-invalid", "true"); else els[key].removeAttribute("aria-invalid");
    }
    function validate(all){
      var bad = [];
      ["name", "url", "icon", "group", "order"].forEach(function(k){
        if(!els[k]) return;
        var msg = check(k);
        if(all || touched[k]) showErr(k, msg);
        if(msg) bad.push(k);
      });
      return bad;
    }
    function refreshUrlHint(){
      var u = cardFormUrl(els.url.value);
      hints.url.textContent = u.error ? "Web address (https://...) or program path (C:\\...\\app.exe)." : u.note;
    }
    function refreshPreview(){
      var curImage = pick ? pick.image() : card.iconImage; /* adc-iconpick: the picture chosen in the form */
      var probe = { icon: els.icon.value.replace(/^\s+|\s+$/g, ""), iconImage: curImage, iconTile: els.iconTile ? els.iconTile.value : card.iconTile };
      preview.classList.remove("adc-has-img");
      setCardIcon(preview, probe);
      if(!preview.textContent && !preview.querySelector("img")){ preview.textContent = "?"; preview.classList.add("adc-cf-empty"); }
      else preview.classList.remove("adc-cf-empty");
      var img = cardIconSrc(probe);
      var web = /^https?:\/\//i.test(cardFormUrl(els.url.value).value || "");
      if(img){
        iconNote.textContent = "Picture: " + curImage + ". The card shows the picture; the emoji is only the fallback if the picture cannot load.";
      } else if(web && pick && pick.removed()){
        iconNote.textContent = "Picture removed; the card shows the emoji. Note: web cards get their site icon filled in automatically, so it comes back after the next start.";
      } else if(web){
        iconNote.textContent = "One emoji, or a picture (Image file\u2026 / Use site icon). Without a picture, the site's icon (favicon) is fetched automatically about a minute after saving and then replaces the emoji.";
      } else {
        iconNote.textContent = "One emoji, or a picture (Image file\u2026). Tip: press Win + . (period) to open the Windows emoji picker.";
      }
      if(pick) pick.paint();
    }
    function setBanner(kind, msg){
      if(!banner) return;
      banner.className = "adc-cf-banner" + (kind ? " " + kind : "");
      banner.textContent = msg || "";
      banner.hidden = !msg;
      if(msg){ try { banner.scrollIntoView({ block: "nearest" }); } catch(e){} }
    }
    function setStatus(kind, msg){
      if(!status) return;
      status.className = "adc-cf-status" + (kind ? " " + kind : "");
      status.textContent = msg || "";
    }
    function syncDirty(){
      if(saving) return;
      setStatus(dirty() ? "dirty" : "", dirty() ? "Unsaved changes" : "");
    }
    function setBusy(on){
      if(!saveBtn) return;
      saveBtn.disabled = !!on;
      saveBtn.textContent = on ? "Saving\u2026" : "Save";
      if(form) form.setAttribute("aria-busy", on ? "true" : "false");
    }
    function alive(){
      var me = document.getElementById("lpModal");
      return !closed && !!form && document.body.contains(form) && !!me && me.classList.contains("show");
    }
    function detach(){
      document.removeEventListener("keydown", onKeyCap, true);
      document.removeEventListener("click", onClickCap, true);
    }
    function finishClose(saved){
      if(closed) return;
      closed = true;
      detach();
      if(typeof onDone === "function"){ onDone(); return; }
      if(lp.closeModal) lp.closeModal();
    }
    function hideConfirm(){ if(confirmBar) confirmBar.hidden = true; }
    function requestClose(){
      if(!dirty() || saving){ finishClose(false); return; }
      if(confirmBar.hidden){
        confirmBar.hidden = false;
        try { confirmBar.scrollIntoView({ block: "nearest" }); } catch(e){}
        var keep = confirmBar.querySelector("button");
        if(keep) keep.focus();
      }
    }
    function settle(p, tries){
      return Promise.resolve(p).then(function(r){
        var live = window.__adcLive;
        var q = live && live.lastSave ? live.lastSave() : p;
        if(q !== p && (tries || 0) < 5) return settle(q, (tries || 0) + 1);
        return r;
      });
    }
    function doSave(){
      if(saving || closed) return;
      hideConfirm();
      if(pick && pick.busy()){ setBanner("err", "The picture is still being saved. Press Save again in a moment."); setStatus("err", "Not saved"); return; } /* adc-iconpick */
      var bad = validate(true);
      if(bad.length){
        setBanner("err", bad.length === 1 ? "Please fix the highlighted field." : "Please fix the " + bad.length + " highlighted fields.");
        setStatus("err", "Not saved");
        if(bad.some(function(k){ return k === "order"; })){ var adv = form.querySelector("details.adc-cf-adv"); if(adv && adv.contains(els.order)) adv.open = true; }
        try { els[bad[0]].focus(); } catch(e){}
        return;
      }
      var m = window.__ADC && window.__ADC.model;
      if(!m || !Array.isArray(m.cards)){
        setBanner("err", "The cockpit data has not loaded yet, so nothing was saved. Wait a moment and press Save again.");
        return;
      }
      var target = isNew ? card : (findById(m.cards, card.id) || card);
      var u = cardFormUrl(els.url.value);
      if(u.value && u.value !== els.url.value.replace(/^\s+|\s+$/g, "")) els.url.value = u.value;
      var name = els.name.value.replace(/^\s+|\s+$/g, "");
      target.name = name;
      var appKey = els.app.value.replace(/^\s+|\s+$/g, ""); if(!appKey || appKey === "New card") appKey = name; target.app = appKey; /* 2026-11-02 newcard fix */
      target.description = els.desc.value;
      target.icon = els.icon.value.replace(/^\s+|\s+$/g, "");
      if(pick) pick.apply(target); /* adc-iconpick: sets/removes iconImage only when the picker changed it */
      if(els.iconTile){ var tileV = cardIconTile({ iconTile: els.iconTile.value }); if(tileV === "light") delete target.iconTile; else target.iconTile = tileV; } /* adc-icontile */
      target.color = els.color.value.replace(/^\s+|\s+$/g, "");
      if(u.value) target.url = u.value;
      else delete target.url;
      if(!lockGroup) target.groupId = els.group.value;
      target.order = num(els.order.value, target.order || 1);
      if(isNew){
        if(!target.id) target.id = mintUuid();
        if(!findById(m.cards, target.id)) m.cards.push(target);
      }
      saving = true;
      setBusy(true);
      setBanner("", "");
      setStatus("busy", "Saving\u2026");
      var slow = setTimeout(function(){
        if(saving && alive()) setStatus("busy", "Still saving\u2026 (the cockpit is busy)");
      }, 6000);
      try { commit({ tray: true }); }
      catch(err){
        clearTimeout(slow);
        saving = false; setBusy(false);
        setStatus("err", "Not saved");
        setBanner("err", "Not saved: the page hit an error while applying the change (" + (err && err.message ? err.message : err) + "). Your edits are still in the form.");
        return;
      }
      var live = window.__adcLive;
      settle(live && live.lastSave ? live.lastSave() : Promise.resolve("saved")).then(function(result){
        clearTimeout(slow);
        saving = false;
        if(closed) return;
        if(result === "blocked"){
          setBusy(false);
          setStatus("err", "Not saved");
          setBanner("err", "Not saved: the card files were changed on disk (by another window or an agent) while this form was open. The latest data is loaded now. Press Save again to apply your edits on top of it.");
          return;
        }
        if(result === "failed"){
          setBusy(false);
          setStatus("err", "Not saved");
          setBanner("err", "Not saved: the cockpit could not write cards.json (the save request failed). Your edits are still in the form. Press Save to try again.");
          return;
        }
        start = snapshot();
        setStatus("ok", "Saved \u2713");
        if(saveBtn) saveBtn.textContent = "Saved";
        setTimeout(function(){ if(alive()) finishClose(true); }, 700);
      }, function(err){
        clearTimeout(slow);
        saving = false;
        if(closed) return;
        setBusy(false);
        setStatus("err", "Not saved");
        setBanner("err", "Not saved: " + (err && err.message ? err.message : "the save did not finish") + ". Your edits are still in the form.");
      });
    }
    function onKeyCap(e){
      if(!alive()){ detach(); return; }
      if(e.key === "Escape"){
        e.preventDefault(); e.stopImmediatePropagation();
        if(pick && pick.closeGrid()) return; /* adc-iconpick: Esc closes the emoji grid first */
        if(confirmBar && !confirmBar.hidden){ hideConfirm(); try { els.name.focus(); } catch(_){} return; }
        requestClose();
        return;
      }
      if((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === "s" || e.key === "S")){
        e.preventDefault(); e.stopImmediatePropagation();
        doSave();
        return;
      }
      if(e.key === "Enter" && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.isComposing){
        var t = e.target;
        if(t && form.contains(t) && (t.tagName === "INPUT" || t.tagName === "SELECT")){
          e.preventDefault(); e.stopImmediatePropagation();
          doSave();
        }
      }
    }
    function onClickCap(e){
      if(!alive()){ detach(); return; }
      var me = document.getElementById("lpModal");
      var t = e.target;
      if(t === me || (t && t.closest && t.closest("#lpModal .lp-modal-x"))){
        e.preventDefault(); e.stopImmediatePropagation();
        requestClose();
      }
    }

    var hint = lockGroup
      ? "This card is already in this group. Set the title and URL. Fields marked * are required. Enter saves, Esc cancels."
      : "Fields marked * are required. Enter saves, Esc cancels.";
    lp.openModal({
      title: isNew ? "Add card" : "Edit card",
      hint: hint,
      buildBody: function(body){
        form = mk("div", "adc-cardform");
        form.setAttribute("role", "group");
        form.setAttribute("aria-label", isNew ? "Add card" : "Edit card");
        banner = mk("div", "adc-cf-banner");
        banner.setAttribute("role", "alert");
        banner.hidden = true;
        form.appendChild(banner);
        confirmBar = mk("div", "adc-cf-confirm");
        confirmBar.hidden = true;
        confirmBar.appendChild(mk("span", "adc-cf-confirm-msg", "You have unsaved changes. Discard them?"));
        var keepBtn = mk("button", "", "Keep editing");
        keepBtn.type = "button";
        keepBtn.addEventListener("click", function(){ hideConfirm(); try { els.name.focus(); } catch(_){} });
        var discardBtn = mk("button", "danger", "Discard changes");
        discardBtn.type = "button";
        discardBtn.addEventListener("click", function(){ finishClose(false); });
        confirmBar.appendChild(keepBtn);
        confirmBar.appendChild(discardBtn);
        form.appendChild(confirmBar);

        row(form, "name", "Title", true, textInput(card.name, "e.g. DigitalOcean"), "The name shown on the card.");
        row(form, "url", "URL", false, textInput(card.url || "", "https://example.com/  or  C:\\Program Files\\App\\app.exe"), "");
        var iconInput = textInput(card.icon, "\uD83D\uDE80");
        iconInput.className = "adc-cf-icon";
        iconInput.setAttribute("aria-label", "Icon emoji");
        var iconCell = row(form, "icon", "Icon", false, iconInput, "");
        var line = mk("div", "adc-cf-iconline");
        iconCell.insertBefore(line, iconInput);
        line.appendChild(iconInput);
        var pv = mk("div", "adc-cf-pvwrap");
        preview = mk("span", "ico adc-cf-preview");
        preview.setAttribute("aria-hidden", "true");
        pv.appendChild(preview);
        pv.appendChild(mk("span", "adc-cf-pvlabel", "Preview"));
        line.appendChild(pv);
        iconNote = hints.icon;
        /* adc-iconpick: buttons under the emoji box + preview; the picture path lives in a hidden input in els,
           so the form's snapshot (dirty check, Unsaved changes, discard prompt) covers it. */
        pick = iconPicker({ card: card, input: iconInput, getUrl: function(){ return cardFormUrl(els.url.value).value || ""; },
          changed: function(){ refreshPreview(); showErr("icon", check("icon")); hideConfirm(); syncDirty(); } });
        iconCell.insertBefore(pick.bar, hints.icon);
        /* adc-icontile: tile behind the icon, exactly three choices. Hidden input so the snapshot/dirty check sees it. */
        var tileHidden = mk("input"); tileHidden.type = "hidden"; tileHidden.className = "adc-cf-icontile"; tileHidden.value = cardIconTile(card);
        var tileRow = mk("div", "adc-cf-tilerow");
        var tileLab = mk("span", "adc-cf-tilelab", "Icon tile"); tileLab.id = "adcCfTileLab";
        var seg = mk("div", "adc-cf-seg"); seg.setAttribute("role", "radiogroup"); seg.setAttribute("aria-labelledby", "adcCfTileLab");
        var tileBtns = [["light", "Light"], ["dark", "Dark"], ["none", "None"]].map(function(c){
          var b2 = mk("button", "adc-cf-segbtn", c[1]); b2.type = "button"; b2.setAttribute("role", "radio"); b2.setAttribute("data-tile", c[0]);
          b2.title = c[0] === "light" ? "Soft off-white tile behind the icon (default)" : c[0] === "dark" ? "Dark tile behind the icon" : "No tile, icon only";
          b2.addEventListener("click", function(){ setTile(c[0]); });
          b2.addEventListener("keydown", function(ev){
            var k = ev.key, order = ["light", "dark", "none"], i = order.indexOf(tileHidden.value);
            if(k === "ArrowRight" || k === "ArrowDown"){ ev.preventDefault(); setTile(order[(i + 1) % 3], true); }
            else if(k === "ArrowLeft" || k === "ArrowUp"){ ev.preventDefault(); setTile(order[(i + 2) % 3], true); }
          });
          seg.appendChild(b2); return b2;
        });
        function paintTileBtns(){ tileBtns.forEach(function(b2){ var on = b2.getAttribute("data-tile") === tileHidden.value; b2.classList.toggle("on", on); b2.setAttribute("aria-checked", on ? "true" : "false"); b2.tabIndex = on ? 0 : -1; }); }
        function setTile(v, focus){ tileHidden.value = v; paintTileBtns(); if(focus){ var f = seg.querySelector("[data-tile=\"" + v + "\"]"); if(f) try { f.focus(); } catch(e){} } refreshPreview(); hideConfirm(); syncDirty(); }
        paintTileBtns();
        tileRow.appendChild(tileLab); tileRow.appendChild(seg);
        line.appendChild(tileRow); /* next to the emoji box and the live preview; the Icon cell children stay as before */
        iconCell.appendChild(tileHidden);
        els.iconTile = tileHidden;
        iconCell.insertBefore(pick.grid, hints.icon);
        iconCell.appendChild(pick.msg);
        iconCell.appendChild(pick.hidden);
        els.iconImage = pick.hidden;
        row(form, "desc", "Description", false, textInput(card.description, "Optional"), "Optional. Small text under the title.");
        if(!lockGroup){
          var sel = mk("select");
          var choices = groupChoices();
          var has = choices.some(function(o){ return o.value === card.groupId; });
          if(!has){
            var ph = mk("option", "", "Choose a group\u2026");
            ph.value = "";
            ph.selected = true;
            sel.appendChild(ph);
          }
          choices.forEach(function(o){
            var opt = mk("option", "", o.label);
            opt.value = o.value;
            if(o.value === card.groupId) opt.selected = true;
            sel.appendChild(opt);
          });
          row(form, "group", "Group", true, sel, "The tray section the card sits in.");
        }
        var orderInput = textInput(card.order || 1, "1");
        orderInput.inputMode = "numeric";
        orderInput.className = "adc-cf-short";
        row(form, "order", "Order", true, orderInput, "Position inside its group (1 = first).");

        var adv = mk("details", "adc-cf-adv");
        adv.appendChild(mk("summary", "", "Advanced (app id, colour)"));
        var advGrid = mk("div", "adc-cf-advgrid");
        adv.appendChild(advGrid);
        row(advGrid, "app", "App id", false, textInput(card.app, card.name || ""), "Internal id the panes use (data-app). Leave empty to use the title.");
        var colorInput = textInput(card.color || "fin", "fin");
        colorInput.className = "adc-cf-short";
        var dl = mk("datalist");
        dl.id = "adcCfColors";
        ["fin", "env-dev", "env-prod"].forEach(function(c){ var o = mk("option"); o.value = c; dl.appendChild(o); });
        colorInput.setAttribute("list", dl.id);
        var colorCell = row(advGrid, "color", "Color class", false, colorInput, "Card border style: fin (default), env-dev or env-prod.");
        colorCell.appendChild(dl);
        form.appendChild(adv);
        body.appendChild(form);

        Object.keys(els).forEach(function(k){
          var el = els[k];
          var ev = el.tagName === "SELECT" ? "change" : "input";
          el.addEventListener(ev, function(){
            if(k === "url"){ refreshUrlHint(); refreshPreview(); }
            if(k === "icon") refreshPreview();
            if(k === "name") els.app.placeholder = els.name.value.replace(/^\s+|\s+$/g, "");
            if(touched[k] || errs[k] && !errs[k].hidden) showErr(k, check(k));
            if(banner && !banner.hidden && banner.classList.contains("err") && !validate(false).length) setBanner("", "");
            hideConfirm();
            syncDirty();
          });
          el.addEventListener("blur", function(ev){
            /* Leaving the field for Save/Cancel: skip blur checks so the layout does not shift under the click (Save validates anyway). */
            var to = ev && ev.relatedTarget;
            if(to && to.closest && to.closest("#lpModalActions, .adc-cf-confirm")) return;
            touched[k] = true;
            if(k === "url"){
              var u = cardFormUrl(el.value);
              if(!u.error && u.value && u.value !== el.value.replace(/^\s+|\s+$/g, "")){ el.value = u.value; refreshUrlHint(); refreshPreview(); syncDirty(); }
            }
            showErr(k, check(k));
          });
        });
        refreshUrlHint();
        refreshPreview();
        start = snapshot();
      },
      actions: [
        { label: "Cancel", onClick: function(){ requestClose(); return true; } },
        { label: "Save", primary: true, onClick: function(){ doSave(); return true; } }
      ]
    });
    var acts = document.getElementById("lpModalActions");
    if(acts){
      saveBtn = acts.querySelector("button.primary");
      status = mk("span", "adc-cf-status");
      status.setAttribute("role", "status");
      status.setAttribute("aria-live", "polite");
      acts.insertBefore(status, acts.firstChild);
    }
    if(form){
      document.addEventListener("keydown", onKeyCap, true);
      document.addEventListener("click", onClickCap, true);
      setTimeout(function(){ if(alive() && els.name){ try { els.name.focus(); els.name.select(); } catch(_){} } }, 40);
    }
  }

  /* Deletes one card from the overlay. */
  function deleteCard(card, onDone){
    confirmBox("Delete card", "Remove " + card.name + " from the tray?", function(){
      window.__ADC.model.cards = window.__ADC.model.cards.filter(function(c){ return c.id !== card.id; });
      commit({ tray: true });
      if(typeof onDone === "function"){ onDone(); return true; }
    }, typeof onDone === "function" ? function(){ onDone(); } : null);
  }

  /* Copy card (adc-copycard): same group, right after the original; saved like any other card edit. */
  function copyCard(card){
    var copy = copyCardAfter(window.__ADC.model.cards, card, mintUuid);
    if(!copy) return null;
    commit({ tray: true });
    return copy;
  }

  /* Edits or creates one group. */
  function editGroup(group, isNew){
    var els = {};
    var parentId = group && group.parentId ? group.parentId : null;
    ask(isNew ? "Add group" : "Edit group", "The title is also the collapse memory key.", function(body){
      els.title = field(body, "Title", group.title);
      els.title.addEventListener("keydown", function(e){
        if(e.key !== "Enter") return;
        e.preventDefault();
        e.stopPropagation();
        var saveBtn = document.querySelector("#lpModalActions button.primary");
        if(saveBtn) saveBtn.click();
      });
      els.order = field(body, "Order", group.order || 1);
      els.collapsed = checkField(body, "Start collapsed (only when this app has no saved state)", group.defaultCollapsed);
      els.showInRail = checkField(body, "show in rail", group.showInRail !== false);
    }, function(){
      var title = els.title.value.trim();
      if(!title) return "Title is required.";
      group.title = title;
      group.order = num(els.order.value, group.order || 1);
      group.defaultCollapsed = !!els.collapsed.checked;
      group.showInRail = !!els.showInRail.checked;
      group.parentId = parentId;
      if(isNew){
        if(group.showInRail !== false) group.showInRail = true;
        group.id = mintId(window.__ADC.model.groups, title);
        window.__ADC.model.groups.push(group);
      }
      commit({ tray: true });
    });
  }

  /* Deletes a group and the cards in it. */
  /* 2026-10-09 empty-only rule (adc-delete-empty-only): every delete path (edit-mode Delete button, menu)
     removes a group only when it has no cards and no subgroups; it never deletes cards or subgroups. */
  function groupContents(model, id){
    return {
      cards: (model.cards || []).filter(function(c){ return c && c.groupId === id; }).length,
      kids: (model.groups || []).filter(function(g){ return g && g.parentId === id; }).length
    };
  }
  function deleteGroup(group){
    var title = group.title || group.id;
    var n0 = groupContents(window.__ADC.model, group.id);
    if(n0.cards || n0.kids){
      var lp0 = window.__ADC && window.__ADC.lp;
      var msg = "Only an empty group can be deleted. " + title + " still has " + n0.cards + " card(s) and " + n0.kids + " subgroup(s); move them out first.";
      if(lp0 && lp0.toast) lp0.toast(msg); else window.alert(msg);
      return;
    }
    confirmBox("Delete group", "Remove the empty group " + title + "?", function(){
      var model = window.__ADC.model;
      var n = groupContents(model, group.id);
      if(n.cards || n.kids){
        var lp1 = window.__ADC && window.__ADC.lp;
        if(lp1 && lp1.toast) lp1.toast(title + " is not empty any more; nothing deleted.");
        return;
      }
      model.groups = model.groups.filter(function(g){ return g && g.id !== group.id; });
      commit({ tray: true });
    });
  }

  /* Keeps the live stage list in step with a tab record. */
  function syncTabRuntime(tab, isNew){
    var lp = window.__ADC.lp;
    if(!lp) return;
    lp.TAB_LABELS[tab.id] = tab.label;
    if(lp.TAB_IDS.indexOf(tab.id) < 0) lp.TAB_IDS.push(tab.id);
    if(isNew){
      lp.ensureTabStage(tab.id);
      if(!lp.stages[tab.id]) lp.stages[tab.id] = lp.defaultStage(tab.id);
    }
  }


  /* Plain names for mark shapes that already exist in the stylesheet. */
  var MARK_WORDS = {
    "mark-launch": "Tilted square",
    "mark-conductor": "Circle",
    "mark-caddy": "Square",
    "mark-runway": "Triangle",
    "mark-sterling": "Pill",
    "mark-phoenix": "Diamond",
    "mark-devops": "Hexagon",
    "mark-balls": "Ring",
    "mark-linear": "Blue bar"
  };
  var EXTRA_WORDS = {
    "launch": "Launch Pad highlight"
  };

  /* Class tokens from the live stylesheet plus classes already stored on tabs. */
  function tokensFromPage(kind){
    var found = [];
    var seen = {};
    function add(v){
      if(!v || seen[v]) return;
      seen[v] = 1;
      found.push(v);
    }
    function walk(rules){
      if(!rules) return;
      for(var i = 0; i < rules.length; i++){
        var rule = rules[i];
        if(rule.cssRules) walk(rule.cssRules);
        var sel = rule.selectorText || "";
        var re = kind === "mark" ? /\.(mark-[A-Za-z0-9_-]+)/g : /\.tab\.([A-Za-z0-9_-]+)/g;
        var m;
        while((m = re.exec(sel))){
          if(kind === "extra" && m[1] === "active") continue;
          add(m[1]);
        }
      }
    }
    var sheets = document.styleSheets;
    for(var s = 0; s < sheets.length; s++){
      try { walk(sheets[s].cssRules); } catch(e){}
    }
    (window.__ADC.model.tabs || []).forEach(function(t){
      if(kind === "mark" && t.mark) add(t.mark);
      if(kind === "extra" && t.extraClass) add(t.extraClass);
    });
    return found;
  }

  function plainMark(tok){
    if(MARK_WORDS[tok]) return MARK_WORDS[tok];
    var tabs = window.__ADC.model.tabs || [];
    for(var i = 0; i < tabs.length; i++){
      if(tabs[i].mark === tok && tabs[i].label) return "Saved mark on " + tabs[i].label;
    }
    return "Saved mark";
  }

  function plainExtra(tok){
    if(EXTRA_WORDS[tok]) return EXTRA_WORDS[tok];
    var tabs = window.__ADC.model.tabs || [];
    for(var i = 0; i < tabs.length; i++){
      if(tabs[i].extraClass === tok && tabs[i].label) return "Saved style on " + tabs[i].label;
    }
    return "Saved style";
  }

  function markOptions(current){
    var opts = [{ value: "", label: "No mark" }];
    tokensFromPage("mark").forEach(function(tok){ opts.push({ value: tok, label: plainMark(tok) }); });
    if(current && !opts.some(function(o){ return o.value === current; })) opts.push({ value: current, label: plainMark(current) });
    return opts;
  }

  function extraOptions(current){
    var opts = [{ value: "", label: "No extra style" }];
    tokensFromPage("extra").forEach(function(tok){ opts.push({ value: tok, label: plainExtra(tok) }); });
    if(current && !opts.some(function(o){ return o.value === current; })) opts.push({ value: current, label: plainExtra(current) });
    return opts;
  }

  /* Draws the tab list inside the open Tabs pop-up. */
  function paintTabList(body){
    body.textContent = "";
    var list = document.createElement("div");
    list.className = "adc-tab-list";
    byOrder(window.__ADC.model.tabs).forEach(function(tab){
      var row = document.createElement("div");
      row.className = "adc-tab-row";
      row.setAttribute("draggable", "true");
      row.setAttribute("data-tab-id", tab.id);
      var grip = document.createElement("span");
      grip.className = "grip";
      grip.setAttribute("aria-hidden", "true");
      var name = document.createElement("span");
      name.className = "name";
      name.textContent = tab.label || tab.id;
      row.appendChild(grip);
      row.appendChild(name);
      var editBtn = document.createElement("button");
      editBtn.type = "button";
      editBtn.textContent = "Edit";
      editBtn.addEventListener("click", function(e){
        e.preventDefault();
        e.stopPropagation();
        openTabForm(tab, false);
      });
      row.appendChild(editBtn);
      if(tab.id !== "launch"){
        var delBtn = document.createElement("button");
        delBtn.type = "button";
        delBtn.className = "danger";
        delBtn.textContent = "Delete";
        delBtn.addEventListener("click", function(e){
          e.preventDefault();
          e.stopPropagation();
          deleteTab(tab);
        });
        row.appendChild(delBtn);
      }
      row.addEventListener("dragstart", function(e){
        if(e.target.closest && e.target.closest("button")){ e.preventDefault(); return; }
        window.__adcTabDrag = true;
        draggingTabId = tab.id;
        row.classList.add("tab-dragging");
        try {
          e.dataTransfer.setData("text/plain", tab.id);
          e.dataTransfer.effectAllowed = "move";
        } catch(err){}
      });
      row.addEventListener("dragend", function(){
        row.classList.remove("tab-dragging");
        list.querySelectorAll(".tab-drop").forEach(function(el){ el.classList.remove("tab-drop"); });
        window.__adcTabDrag = false;
        draggingTabId = "";
      });
      row.addEventListener("dragover", function(e){
        if(!draggingTabId) return;
        e.preventDefault();
        if(draggingTabId !== tab.id) row.classList.add("tab-drop");
      });
      row.addEventListener("dragleave", function(){ row.classList.remove("tab-drop"); });
      row.addEventListener("drop", function(e){
        if(!draggingTabId) return;
        e.preventDefault();
        e.stopPropagation();
        row.classList.remove("tab-drop");
        var fromId = draggingTabId;
        moveTabBefore(fromId, tab.id);
      });
      list.appendChild(row);
    });
    body.appendChild(list);
  }

  /* One pop-up listing every tab in strip order. */
  function openTabList(){
    var lp = window.__ADC && window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    lp.openModal({
      title: "Tabs",
      hint: "Same order as the tab strip, left to right. Drag a row to reorder. That saves immediately.",
      buildBody: function(body){
        tabListBody = body;
        paintTabList(body);
      },
      actions: [
        { label: "Add tab", primary: true, onClick: function(){
          openTabForm({ label: "", mark: "" }, true);
          return true;
        } },
        { label: "Close", onClick: function(){ tabListBody = null; } }
      ]
    });
  }

  /* Add or edit one tab. Mark and style are dropdowns. Order is assigned, not typed. */
  function openTabForm(tab, isNew){
    var els = {};
    var lp = window.__ADC && window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    lp.openModal({
      title: isNew ? "Add tab" : "Edit tab",
      hint: "The sample panes on the original tabs are not rewritten.",
      buildBody: function(body){
        els.label = field(body, "Label", tab.label || "");
        els.mark = selectField(body, "Mark shape", markOptions(tab.mark || ""), tab.mark || "");
      },
      actions: [
        { label: "Back", onClick: function(){ openTabList(); return true; } },
        { label: "Save", primary: true, onClick: function(){
          var label = els.label.value.trim();
          if(!label){ if(lp.toast) lp.toast("Label is required."); return true; }
          tab.label = label;
          tab.mark = els.mark.value;
          delete tab.extraClass;
          if(isNew){
            tab.order = nextTabOrder();
            tab.id = mintId(window.__ADC.model.tabs, label);
            tab.active = false;
            window.__ADC.model.tabs.push(tab);
          }
          syncTabRuntime(tab, isNew);
          commit({ tabs: true });
          openTabList();
          return true;
        } }
      ]
    });
  }

  /* Edits or creates one tab. Launch Pad's id stays put. */
  function editTab(tab, isNew){
    openTabForm(tab, isNew);
  }

  /* Deletes a tab other than Launch Pad, then returns to the tab list. */
  function deleteTab(tab){
    if(tab.id === "launch"){
      var lp0 = window.__ADC.lp;
      if(lp0 && lp0.toast) lp0.toast("Launch Pad stays. Edit its label instead.");
      openTabList();
      return;
    }
    var lp = window.__ADC.lp;
    if(!lp || !lp.openModal) return;
    lp.openModal({
      title: "Delete tab",
      hint: "Remove the " + tab.label + " tab from this app's saved copy?",
      actions: [
        { label: "Back", onClick: function(){ openTabList(); return true; } },
        { label: "Delete", primary: true, onClick: function(){
          window.__ADC.model.tabs = window.__ADC.model.tabs.filter(function(t){ return t.id !== tab.id; });
          if(window.__ADC.lp && window.__ADC.lp.removeTabStage) window.__ADC.lp.removeTabStage(tab.id);
          commit({ tabs: true });
          openTabList();
          return true;
        } }
      ]
    });
  }

  /* Edits or creates one stage layout. */
  function editLayout(layout, isNew){
    var els = {};
    ask(isNew ? "Add layout" : "Edit layout", "Count is the number of slots. Cols and rows are track weights.", function(body){
      if(isNew) els.id = field(body, "Id", layout.id || "");
      els.label = field(body, "Label", layout.label);
      els.title = field(body, "Tooltip", layout.title);
      els.order = field(body, "Order", layout.order || 1);
      els.count = field(body, "Slot count", layout.count || 1);
      els.cls = field(body, "Grid class", layout.cls || "");
      els.mini = field(body, "Mini class", layout.mini || "");
      els.cells = field(body, "Mini cells", layout.miniCells || layout.count || 1);
      els.cols = field(body, "Columns", (layout.cols || []).join(", "));
      els.rows = field(body, "Rows", (layout.rows || []).join(", "));
    }, function(){
      var id = isNew ? els.id.value.trim() : layout.id;
      if(!id) return "Id is required.";
      if(isNew && findById(window.__ADC.model.stages.layouts, id)) return "That id already exists.";
      var count = num(els.count.value, 1);
      if(count < 1) return "Slot count must be at least 1.";
      layout.id = id;
      layout.label = els.label.value.trim() || id;
      layout.title = els.title.value.trim() || layout.label;
      layout.order = num(els.order.value, layout.order || 1);
      layout.count = count;
      layout.cls = els.cls.value.trim() || ("layout-" + id);
      layout.mini = els.mini.value.trim();
      layout.miniCells = num(els.cells.value, count);
      var cols = numList(els.cols.value);
      var rows = numList(els.rows.value);
      layout.cols = cols.length ? cols : [1];
      layout.rows = rows.length ? rows : [1];
      if(isNew) window.__ADC.model.stages.layouts.push(layout);
      var lp = window.__ADC.lp;
      if(lp){
        Object.keys(lp.stages).forEach(function(tab){
          if(lp.stages[tab].sizes) delete lp.stages[tab].sizes[layout.id];
        });
      }
      commit({ layouts: true });
    });
  }

  /* Deletes a layout and points any stage using it at another one. */
  function deleteLayout(layout){
    var list = window.__ADC.model.stages.layouts;
    if(list.length < 2){
      var lp0 = window.__ADC.lp;
      if(lp0 && lp0.toast) lp0.toast("Keep at least one layout.");
      return;
    }
    confirmBox("Delete layout", "Remove the " + (layout.label || layout.id) + " layout?", function(){
      var other = list.filter(function(L){ return L.id !== layout.id; })[0];
      window.__ADC.model.stages.layouts = other ? list.filter(function(L){ return L.id !== layout.id; }) : list;
      var lp = window.__ADC.lp;
      if(lp){
        Object.keys(lp.stages).forEach(function(tab){
          if(lp.stages[tab].layout === layout.id) lp.stages[tab].layout = other.id;
        });
      }
      commit({ layouts: true });
    });
  }

  /* Edits the seed slots for Launch Pad and for every other tab. */
  function editDefaults(){
    var defs = window.__ADC.model.stages.defaults || { launch: { layout: "half", slots: [] }, other: { layout: "single", slots: ["@native"] } };
    var els = {};
    var options = window.__ADC.model.stages.layouts.map(function(L){ return { value: L.id, label: L.label || L.id }; });
    ask("Stage defaults", "Used when a tab has no saved stage yet. Saving also applies the Launch Pad default now.", function(body){
      els.launchLayout = selectField(body, "Launch Pad layout", options, (defs.launch && defs.launch.layout) || "half");
      els.launchSlots = field(body, "Launch Pad slots (comma separated app names)", ((defs.launch && defs.launch.slots) || []).join(", "));
      els.otherLayout = selectField(body, "Other tabs layout", options, (defs.other && defs.other.layout) || "single");
      els.otherSlots = field(body, "Other tabs slots", ((defs.other && defs.other.slots) || []).join(", "));
    }, function(){
      window.__ADC.model.stages.defaults = {
        launch: {
          layout: els.launchLayout.value,
          slots: els.launchSlots.value.split(",").map(function(s){ return s.trim(); }).filter(Boolean)
        },
        other: {
          layout: els.otherLayout.value,
          slots: els.otherSlots.value.split(",").map(function(s){ return s.trim(); }).filter(Boolean)
        }
      };
      var lp = window.__ADC.lp;
      if(lp && lp.stages.launch){
        lp.stages.launch.layout = window.__ADC.model.stages.defaults.launch.layout;
        lp.stages.launch.slots = window.__ADC.model.stages.defaults.launch.slots.slice();
        if(lp.switchStage) lp.switchStage("launch");
      }
      commit({ layouts: true });
    });
  }

  /* Clears the browser overlay and reloads the JSON seed. */
  function resetData(){
    confirmBox("Reset page data", "Clears edits saved in this app and loads the JSON files again.", function(){
      try { localStorage.removeItem(OVERLAY_KEY); } catch(e){}
      location.reload();
    });
  }

  /* Adds the edit controls. View mode does not call this, so those controls are absent. */
  function mountEditControls(){
    var model = window.__ADC.model;
    if(!model) return;
    var scroll = document.querySelector(".lp-tray-scroll");
    if(scroll){
      var bar = document.createElement("div");
      bar.className = "adc-crudbar";
      bar.appendChild(mini("Add group", function(){ editGroup({ title: "", order: model.groups.length + 1, defaultCollapsed: false }, true); }));
      bar.appendChild(mini("Reset", resetData, true));
      scroll.insertBefore(bar, scroll.firstChild);
    }
    document.querySelectorAll("details.lp-group").forEach(function(d){
      var group = findById(model.groups, d.getAttribute("data-group-id"));
      if(!group) return;
      var row = document.createElement("div");
      row.className = "adc-group-bar";
      row.appendChild(mini("Edit", function(){ editGroup(group, false); }));
      row.appendChild(mini("Add card", function(){ addCardInGroup(group); }));
      // 2026-10-09: "Add subgroup" moved to the group right-click menu (works without edit mode).
      row.appendChild(mini("Delete", function(){ deleteGroup(group); }, true));
      var body = d.querySelector(".lp-body");
      if(body) d.insertBefore(row, body);
    });
    document.querySelectorAll(".lp-tile[data-card-id]").forEach(function(tile){
      var card = findById(model.cards, tile.getAttribute("data-card-id"));
      if(!card) return;
      tile.appendChild(mini("Edit", function(){ editCard(card, false); }, false, true));
      tile.appendChild(mini("Delete", function(){ deleteCard(card); }, true, true));
    });
    var toggle = document.getElementById("lpTabsEnd") || document.getElementById("lpToolbarToggle");
    if(toggle){
      var editTabsBtn = mini("Edit", openTabList);
      editTabsBtn.setAttribute("aria-label", "Edit tabs");
      editTabsBtn.title = "Add, edit, delete, or review tabs";
      toggle.parentNode.insertBefore(editTabsBtn, toggle);
    }
    var actions = document.querySelector(".lp-toolbar-actions");
    if(actions){
      var stageBar = document.createElement("div");
      stageBar.className = "adc-crudbar";
      stageBar.appendChild(mini("Add layout", function(){
        editLayout({ id: "", label: "", title: "", order: model.stages.layouts.length + 1, count: 1, cls: "", mini: "", miniCells: 1, cols: [1], rows: [1] }, true);
      }));
      stageBar.appendChild(mini("Stage defaults", editDefaults));
      actions.insertBefore(stageBar, actions.firstChild);
    }
    document.querySelectorAll("#lpLayoutBtns .lp-layout-btn").forEach(function(btn){
      var layout = findById(model.stages.layouts, btn.getAttribute("data-layout"));
      if(!layout) return;
      btn.appendChild(mini("Edit", function(){ editLayout(layout, false); }, false, true));
      btn.appendChild(mini("Delete", function(){ deleteLayout(layout); }, true, true));
    });
  }

  /* Turns edit mode on or off. It is not stored; every load starts off. */
  function syncAdminBanner(){
    var header = document.querySelector(".app > header") || document.querySelector("header");
    var banner = document.getElementById("adcAdminBanner");
    if(!editing){
      if(banner) banner.remove();
      return;
    }
    if(!header || banner) return;
    banner = document.createElement("div");
    banner.id = "adcAdminBanner";
    banner.className = "adc-admin-banner";
    banner.setAttribute("role", "status");
    banner.textContent = "admin mode";
    header.appendChild(banner);
  }

  function setEditMode(on){
    var btn = document.getElementById("adcEditToggle");
    if(on && !(window.__ADC && window.__ADC.model)){
      editing = false;
      document.body.classList.remove("adc-editing");
      if(btn){
        btn.textContent = "Edit";
        btn.setAttribute("aria-pressed", "false");
        btn.title = "Unlocks add, edit, and delete";
        btn.setAttribute("aria-label", "Edit. Unlocks add, edit, and delete.");
      }
      var hint0 = document.getElementById("adcModeHint");
      if(hint0) hint0.textContent = "Unlocks add, edit, and delete";
      syncAdminBanner();
      return;
    }
    editing = !!on;
    document.body.classList.toggle("adc-editing", editing);
    syncAdminBanner();
    if(btn){
      btn.textContent = editing ? "Done" : "Edit";
      btn.setAttribute("aria-pressed", editing ? "true" : "false");
      btn.title = editing ? "Hides add, edit, and delete" : "Unlocks add, edit, and delete";
      btn.setAttribute("aria-label", editing ? "Done. Hides add, edit, and delete." : "Edit. Unlocks add, edit, and delete.");
    }
    var hint = document.getElementById("adcModeHint");
    if(hint) hint.textContent = editing ? "Add, edit, and delete are on" : "Unlocks add, edit, and delete";
    clearEditControls();
    if(editing) mountEditControls();
    equalizeTrayCards(true);
    document.querySelectorAll("#tabs .tab[data-view]").forEach(function(chip){
      chip.setAttribute("draggable", editing ? "true" : "false");
    });
  }

  /* Wires the header toggle. The first click reveals CRUD. */
  function wireToggle(){
    var btn = document.getElementById("adcEditToggle");
    if(!btn || btn.dataset.adcBound) return;
    btn.dataset.adcBound = "1";
    btn.addEventListener("click", function(){ setEditMode(!editing); });
  }

  /* One row in the Maintain list. Move stays inside that row's group. */
  function maintainRow(card, idx, rows){
    var row = document.createElement("div");
    row.className = "adc-maintain-row";
    var ico = document.createElement("span");
    ico.className = "ico";
    setCardIcon(ico, card);
    var meta = document.createElement("div");
    meta.className = "meta";
    var name = document.createElement("div");
    name.className = "name";
    name.textContent = card.name || card.app || "(unnamed)";
    var url = document.createElement("div");
    url.className = "url";
    url.textContent = card.url || "No URL";
    meta.appendChild(name);
    meta.appendChild(url);
    var acts = document.createElement("div");
    acts.className = "acts";
    function btn(label, fn, danger, disabled){
      var b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      if(danger) b.className = "danger";
      if(disabled) b.disabled = true;
      b.addEventListener("click", function(e){
        e.preventDefault();
        e.stopPropagation();
        fn();
      });
      acts.appendChild(b);
    }
    btn("Move up", function(){ moveMaintainCard(card, -1, rows); }, false, idx === 0);
    btn("Move down", function(){ moveMaintainCard(card, 1, rows); }, false, idx === rows.length - 1);
    btn("Edit", function(){ editCard(card, false, openMaintain); }, false, false);
    btn("Delete", function(){ deleteCard(card, openMaintain); }, true, false);
    row.appendChild(ico);
    row.appendChild(meta);
    row.appendChild(acts);
    return row;
  }

  /* Writes a new order for the cards in one group and redraws the tray. */
  function moveMaintainCard(card, dir, rows){
    var i = -1, n;
    for(n = 0; n < rows.length; n++) if(rows[n].id === card.id) i = n;
    var j = i + dir;
    if(i < 0 || j < 0 || j >= rows.length) return;
    var next = rows.slice();
    var item = next.splice(i, 1)[0];
    next.splice(j, 0, item);
    for(n = 0; n < next.length; n++) next[n].order = n + 1;
    commit({ tray: true });
    openMaintain();
  }

  /* Lists every Launch Pad card so Director can add, edit, remove, or reorder. */
  function openMaintain(){
    var lp = window.__ADC && window.__ADC.lp;
    var model = window.__ADC && window.__ADC.model;
    if(!lp || !lp.openModal || !model){
      if(lp && lp.toast) lp.toast("Launch Pad cards are not loaded yet.");
      return;
    }
    lp.openModal({
      title: "Maintain Launch Pad",
      hint: "Edit, remove, or reorder cards here. Add a card from that group's row while Edit is on. A change is saved in this app.",
      buildBody: function(body){
        var wrap = document.createElement("div");
        wrap.className = "adc-maintain";
        var seen = {};
        function addGroup(title, groupRows){
          if(!groupRows.length) return;
          var h = document.createElement("div");
          h.className = "adc-maintain-group";
          h.textContent = title;
          wrap.appendChild(h);
          groupRows.forEach(function(card, idx){
            seen[card.id] = 1;
            wrap.appendChild(maintainRow(card, idx, groupRows));
          });
        }
        function listGroups(list, prefix){
          list.forEach(function(g){
            var label = prefix ? prefix + " / " + (g.title || g.id) : (g.title || g.id);
            addGroup(label, byOrder(model.cards.filter(function(c){ return c.groupId === g.id; })));
            listGroups(childGroupsOf(model.groups, g.id), label);
          });
        }
        listGroups(rootGroups(model.groups), "");
        addGroup("No group", byOrder(model.cards.filter(function(c){ return !seen[c.id]; })));
        if(!model.cards.length){
          var empty = document.createElement("p");
          empty.className = "hint";
          empty.textContent = "No cards yet. Add one.";
          wrap.appendChild(empty);
        }
        body.appendChild(wrap);
      },
      actions: [
        { label: "Close", onClick: function(){} }
      ]
    });
  }

  /* Puts Maintain on the layout toolbar and opens the card list. */
  function wireMaintain(){
    var actions = document.querySelector(".lp-toolbar-actions");
    var btn = document.getElementById("adcMaintain");
    if(!btn && actions){
      btn = document.createElement("button");
      btn.type = "button";
      btn.className = "lp-ghost lp-action";
      btn.id = "adcMaintain";
      btn.title = "Edit, remove, or reorder Launch Pad cards";
      btn.textContent = "Maintain";
      actions.insertBefore(btn, actions.firstChild);
    }
    if(!btn || btn.dataset.adcBound) return;
    btn.dataset.adcBound = "1";
    btn.addEventListener("click", function(e){
      e.stopPropagation();
      openMaintain();
    });
  }

  /* Card addresses come from the JSON files. A typed http or https address is kept. A missing or file: address is replaced by the seed url. */
  function applySeedUrls(model, seed){
    if(!model || model === seed || !seed || !Array.isArray(model.cards) || !Array.isArray(seed.cards)) return model;
    seed.cards.forEach(function(s){
      if(!s || !s.id || !s.url) return;
      var card = findById(model.cards, s.id);
      if(!card) return;
      var current = card.url ? String(card.url).replace(/^\s+|\s+$/g, "") : "";
      if(!current || /^file:/i.test(current)) card.url = s.url;
      if(s.id === "tradingview" && current.indexOf("NASDAQ") < 0) card.url = s.url;
    });
    return model;
  }

  /* Drops retired cards from a saved browser copy so a refresh cannot bring them back. */
  function retireRemovedCards(model){
    if(!model || !Array.isArray(model.cards)) return false;
    var retiredIds = {
      "bill-calendar": 1,
      "statements": 1,
      "failed-payments": 1,
      "subscriptions": 1,
      "accounts": 1,
      "exports": 1,
      "thinkorswim": 1,
      "trade-journal": 1,
      "positions": 1
    };
    var retiredNames = {
      "bill calendar": 1,
      "statements": 1,
      "failed payments": 1,
      "subscriptions": 1,
      "accounts": 1,
      "exports": 1,
      "thinkorswim": 1,
      "trade journal": 1,
      "positions": 1
    };
    function label(v){ return retiredNames[String(v || "").replace(/^\s+|\s+$/g, "").toLowerCase()] === 1; }
    function deadYnab(c){
      var id = String(c.id || "").toLowerCase();
      var name = String(c.name || "").toLowerCase();
      var app = String(c.app || "").toLowerCase();
      if(id !== "ynab" && name !== "ynab" && app !== "ynab") return false;
      var url = c.url ? String(c.url).replace(/^\s+|\s+$/g, "") : "";
      var group = String(c.groupId || c.group || "").toLowerCase();
      return group === "banking";
    }
    var before = model.cards.length;
    model.cards = model.cards.filter(function(c){
      if(!c) return false;
      if(retiredIds[String(c.id || "").toLowerCase()]) return false;
      if(label(c.app) || label(c.name)) return false;
      if(deadYnab(c)) return false;
      return true;
    });
    var changed = model.cards.length !== before;
    return changed;
  }

  /* Drops known mockup tabs from a saved overlay. User-added tabs, including finances, stay. */
  function dropMockupTabs(model){
    if(!model) return false;
    if(!Array.isArray(model.tabs)){ model.tabs = []; return false; }
    var mockup = { main:1, conductor:1, caddy:1, runway:1, sterling:1, phoenix:1, devops:1, balls:1, linear:1 };
    var before = model.tabs.length;
    model.tabs = model.tabs.filter(function(t){
      if(!t || !t.id) return false;
      return mockup[String(t.id).toLowerCase()] !== 1;
    });
    return model.tabs.length !== before;
  }

  /* Stops the Launch Pad extraClass from being stored or painted. */
  function stripTabExtraClass(model){
    if(!model || !Array.isArray(model.tabs)) return false;
    var changed = false;
    model.tabs.forEach(function(t){
      if(t && Object.prototype.hasOwnProperty.call(t, "extraClass")){
        delete t.extraClass;
        changed = true;
      }
    });
    return changed;
  }


  /* Puts the seed YNAB card into Finances when a saved browser copy is missing it. */
  function ensureFinancesYnab(model, seed){
    if(!model || !seed || !Array.isArray(model.cards) || !Array.isArray(seed.cards)) return false;
    var ynabId = "d88a15d9-b864-48ea-b499-9160d969f078";
    var present = model.cards.some(function(c){
      if(!c) return false;
      if(c.id === ynabId) return true;
      if(String(c.groupId || "").toLowerCase() !== "finances") return false;
      var name = String(c.name || "").toLowerCase();
      var app = String(c.app || "").toLowerCase();
      return name === "ynab" || app === "ynab";
    });
    if(present) return false;
    var src = null;
    seed.cards.forEach(function(s){
      if(s && s.id === ynabId) src = s;
    });
    if(!src) return false;
    var copy = {};
    Object.keys(src).forEach(function(k){ copy[k] = src[k]; });
    model.cards.push(copy);
    return true;
  }

  /* Starts the page from the JSON files. adc-page-data does not replace cards, groups, tabs, or stages. */
  function start(seed){
    window.__ADC = window.__ADC || {};
    window.__ADC.seed = seed;
    window.__ADC.model = seed;
    renderTabs();
    if(window.__adcBindViewTabs) window.__adcBindViewTabs();
    renderLayoutButtons();
    renderRail();
    renderTray();
    if(typeof window.__initLaunchPad === "function") window.__initLaunchPad();
    bindGroupCollapse();
    restoreActiveTab();
    wireToggle();
    wireMaintain();
    bindGroupCardDnD();
    bindGroupReorder();
    bindCardWidths();
    setTimeout(ensureFavicons, 3000);
  }

  /* Starts the stage chrome when fetch fails. The tray stays empty of cards. */
  function startWithoutData(){
    showLoadNote();
    if(typeof window.__initLaunchPad === "function") window.__initLaunchPad();
    restoreActiveTab();
    wireToggle();
    wireMaintain();
  }

  /* Boot: reads every data file fresh, remembers its exact text as the save baseline, then starts
     the live poll. When a file cannot be read, the page retries and reloads once the files read. */
  fetchTexts(LIVE_FILES).then(function(texts){
    var seed = seedFromTexts(texts);
    rememberBase(texts);
    start(seed);
    startLivePoll();
  }).catch(function(){
    startWithoutData();
    if(location.protocol === "http:" || location.protocol === "https:"){
      var retry = setInterval(function(){
        fetchTexts(LIVE_FILES).then(function(texts){
          seedFromTexts(texts);
          clearInterval(retry);
          location.reload();
        }).catch(function(){});
      }, LIVE_POLL_MS);
    }
  });
})();

/* Popups are page HTML, but WebView2 panes and embedded desktop apps are native windows that
   always paint over the page. While any popup is open the host hides those windows, and it
   shows them again when the popup closes. This tells the host when that state changes. */
(function(){
  var host = window.chrome && window.chrome.webview;
  if(!host || typeof host.postMessage !== "function") return;
  var POPUPS = ".lp-modal-backdrop.show, .lp-dd.open, .adc-ctx.open";
  var last = null, queued = false;
  function shown(el){
    if(!el.getClientRects().length) return false;
    var cs = getComputedStyle(el);
    return cs.visibility !== "hidden" && cs.display !== "none";
  }
  function anyPopup(){
    /* 2026-10-09: while a card or pane is dragged, the native pane views and embedded app windows
       are hidden like for a popup, so a drop on a filled pane reaches the page and replaces it. */
    if(document.body && document.body.classList.contains("adc-dragging")) return true;
    var list = document.querySelectorAll(POPUPS);
    for(var i = 0; i < list.length; i++){ if(shown(list[i])) return true; }
    return false;
  }
  /* 2026-10-11 adc-v156: menus send their rectangles so the host hides only the panes they cover; a dialog
     backdrop or a drag still covers the whole page ("full"). */
  var MENUS = ".lp-dd.open, .adc-ctx.open";
  function payload(){
    var open = anyPopup();
    var msg = { type: "adc-modal", open: open, vw: window.innerWidth, vh: window.innerHeight };
    if(!open) return msg;
    var full = document.body.classList.contains("adc-dragging");
    var bd = document.querySelectorAll(".lp-modal-backdrop.show");
    for(var b = 0; b < bd.length; b++){ if(shown(bd[b])) full = true; }
    var rects = [];
    if(!full){
      var list = document.querySelectorAll(MENUS);
      for(var i = 0; i < list.length; i++){
        if(!shown(list[i])) continue;
        var r = list[i].getBoundingClientRect();
        if(r.width > 0 && r.height > 0) rects.push({ x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) });
      }
      if(!rects.length) full = true;
    }
    msg.full = full;
    msg.rects = full ? [] : rects;
    return msg;
  }
  function report(force){
    queued = false;
    var msg = payload();
    var key = JSON.stringify(msg);
    if(!force && key === last) return;
    last = key;
    try { host.postMessage(msg); } catch(e){}
    if(!msg.open) setTimeout(clearSnaps, 250);
  }
  /* adc-v156: a snapshot of a hidden pane, shown in its place (under the menu) until the menu closes. */
  function clearSnaps(){
    if(anyPopup()) return;
    document.querySelectorAll(".adc-pane-snap").forEach(function(n){ n.remove(); });
  }
  function showSnap(d){
    if(!anyPopup()) return;
    var body = document.querySelector('.lp-slot[data-slot="' + d.slot + '"] [data-pane-body]');
    if(!body) return;
    var r = body.getBoundingClientRect();
    if(r.width < 8 || r.height < 8) return;
    var img = document.querySelector('.adc-pane-snap[data-slot="' + d.slot + '"]');
    if(!img){ img = document.createElement("img"); img.className = "adc-pane-snap"; img.alt = ""; img.setAttribute("data-slot", String(d.slot)); img.draggable = false; document.body.appendChild(img); }
    img.style.left = r.left + "px"; img.style.top = r.top + "px"; img.style.width = r.width + "px"; img.style.height = r.height + "px";
    img.src = String(d.src || "");
  }
  window.__adcPaneSnap = { show: showSnap, clear: clearSnaps };
  try {
    host.addEventListener("message", function(e){
      var d = e && e.data;
      if(!d || typeof d !== "object") return;
      if(d.type === "adc-pane-snap" && typeof d.src === "string" && d.src.indexOf("data:image/") === 0) showSnap(d);
      else if(d.type === "adc-pane-snap-clear") setTimeout(clearSnaps, 150);
    });
  } catch(e){}
  function queue(){
    if(queued) return;
    queued = true;
    requestAnimationFrame(function(){ report(false); });
  }
  function begin(){
    new MutationObserver(queue).observe(document.body, {
      subtree: true, childList: true, attributes: true,
      attributeFilter: ["class", "open", "hidden", "aria-hidden", "style"]
    });
    report(true);
  }
  if(document.body) begin(); else document.addEventListener("DOMContentLoaded", begin);
})();

/* 2026-10-09 group tabs + detach (adc-gtabs). A group can open as its own tab (its cards placed in
   panes). Tabs stay alive when hidden (WV2 host keeps panes and programs per tab). A tab can be
   detached into its own window (WV2: new native window, panes and programs moved, not reloaded;
   HTML: new browser window). Closing the detached window re-attaches the tab. */
(function(){
  var IS_WV2 = !!(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function");
  var SOLO = "";
  try { SOLO = new URLSearchParams(location.search || "").get("solo") || ""; } catch(e){}
  var DET_KEY = "adc-detached-tabs";
  var detached = {};
  function post(msg){ try { if(IS_WV2) window.chrome.webview.postMessage(msg); } catch(e){} }
  function lp(){ return window.__ADC && window.__ADC.lp; }
  function model(){ return window.__ADC && window.__ADC.model; }
  function toast(t){ var l = lp(); if(l && l.toast) l.toast(t); }
  function loadDet(){ try { return JSON.parse(localStorage.getItem(DET_KEY) || "{}") || {}; } catch(e){ return {}; } }
  function saveDet(){ if(!IS_WV2 || SOLO) return; try { localStorage.setItem(DET_KEY, JSON.stringify(detached)); } catch(e){} }
  function tabById(id){
    var m = model(); if(!m || !Array.isArray(m.tabs)) return null;
    for(var i = 0; i < m.tabs.length; i++) if(m.tabs[i] && m.tabs[i].id === id) return m.tabs[i];
    return null;
  }
  /* Host messages for a tab that lives in another window are not sent (see hostSpaceOk users). */
  window.__adcHostSpaceOk = function(tab){
    if(SOLO) return tab === SOLO;
    return !detached[tab];
  };
  window.__adcSoloTab = SOLO;

  function pickLayout(n){
    var l = lp(); var L = (l && l.LAYOUTS) || {};
    var prefer = ["single", "half", "three", "l4", "l3", "l2", "3367"];
    var best = "", bestCount = 0, max = "", maxCount = 0;
    Object.keys(L).sort(function(a, b){ return prefer.indexOf(a) - prefer.indexOf(b); }).forEach(function(k){
      var c = (L[k] && L[k].count) || 0;
      if(c > maxCount){ max = k; maxCount = c; }
      if(c >= n && (!best || c < bestCount)){ best = k; bestCount = c; }
    });
    return best ? { id: best, count: bestCount } : { id: max || "single", count: maxCount || 1 };
  }

  function openGroupAsTab(groupId){
    var m = model(), l = lp();
    if(!m || !l || !Array.isArray(m.groups)) return;
    var group = null;
    m.groups.forEach(function(g){ if(g && g.id === groupId) group = g; });
    if(!group) return;
    var id = "g-" + String(groupId).replace(/[^A-Za-z0-9_-]/g, "").slice(0, 40);
    if(tabById(id)){
      if(detached[id]){ requestDetach(id, group.title || id, false); return; }
      if(window.__activateView) window.__activateView(id);
      return;
    }
    var details = document.querySelector('details.lp-group[data-group-id="' + groupId.replace(/"/g, "") + '"]');
    var tiles = details ? details.querySelectorAll(":scope > .lp-body > .lp-tile") : [];
    var cards = [];
    for(var i = 0; i < tiles.length; i++){
      var app = tiles[i].getAttribute("data-app");
      if(app) cards.push({ app: app, cardId: tiles[i].getAttribute("data-card-id") || "", url: tiles[i].getAttribute("data-url") || "" });
    }
    var lay = pickLayout(Math.max(1, cards.length));
    var slots = [], loads = [];
    for(var s = 0; s < lay.count; s++){
      var c = cards[s];
      slots.push(c ? c.app : null);
      loads.push(c ? { cardId: c.cardId, url: c.url } : null);
    }
    var max = 0;
    m.tabs.forEach(function(t){ var n = parseInt(t && t.order, 10); if(isFinite(n) && n > max) max = n; });
    var tab = { id: id, label: group.title || id, order: max + 1, mark: "mark-launch", group: groupId };
    m.tabs.push(tab);
    l.TAB_LABELS[id] = tab.label;
    if(l.TAB_IDS.indexOf(id) < 0) l.TAB_IDS.push(id);
    l.ensureTabStage(id);
    l.stages[id] = { layout: lay.id, slots: slots, sizes: {}, loads: loads, activeSlot: null, drag: null };
    if(l.persistStages) l.persistStages();
    if(window.__adcCommit) window.__adcCommit({ tabs: true });
    if(window.__activateView) window.__activateView(id);
    toast("Opened " + tab.label + " as a tab" + (cards.length > lay.count ? " (first " + lay.count + " cards)" : ""));
  }
  window.__adcOpenGroupAsTab = openGroupAsTab;

  function closeTab(id){
    var m = model(), l = lp();
    var t = tabById(id);
    if(!m || !l || !t) return;
    post({ type: "adc-space-close", space: id });
    delete detached[id]; saveDet();
    m.tabs = m.tabs.filter(function(x){ return x && x.id !== id; });
    if(l.removeTabStage) l.removeTabStage(id);
    if(l.persistStages) l.persistStages();
    if(window.__adcCommit) window.__adcCommit({ tabs: true });
    toast("Closed tab " + (t.label || id));
  }
  window.__adcCloseTab = closeTab;

  function firstAttachedTab(except){
    var m = model(); var out = "launch";
    if(m && Array.isArray(m.tabs)){
      m.tabs.slice().sort(function(a, b){ return (a.order || 0) - (b.order || 0); }).some(function(t){
        if(t && t.id !== except && !detached[t.id]){ out = t.id; return true; }
        return false;
      });
    }
    return out;
  }

  function detachTab(id){
    var t = tabById(id);
    if(!t || SOLO) return;
    var active = document.querySelector('.tab.active[data-view="' + id + '"]');
    if(IS_WV2){
      // the tab is hidden here only when the host confirms the new window (older builds ignore the message)
      requestDetach(id, t.label || id, true);
      return;
    } else {
      var url = location.pathname + "?solo=" + encodeURIComponent(id);
      var w = window.open(url, "adc-solo-" + id, "popup,width=" + Math.round(screen.availWidth * 0.7) + ",height=" + Math.round(screen.availHeight * 0.8));
      if(!w){ toast("The browser blocked the new window. Allow pop-ups for the cockpit."); return; }
      detached[id] = 1;
    }
    decorate();
    if(active && window.__activateView) window.__activateView(firstAttachedTab(id));
    toast("Detached " + (t.label || id) + " (close that window to put it back)");
  }
  window.__adcDetachTab = detachTab;
  var pendingDetach = {};
  function requestDetach(id, label, user){
    pendingDetach[id] = { user: user, t: setTimeout(function(){
      delete pendingDetach[id];
      if(user) toast("Detach needs the new cockpit build: close and reopen the cockpit once.");
      if(detached[id]){ delete detached[id]; saveDet(); decorate(); }
    }, 2500) };
    post({ type: "adc-detach-space", space: id, label: label });
  }
  /* The WV2 host calls this after it opened the tab's own window and moved its panes there. */
  window.__adcDetachAck = function(id){
    var p = pendingDetach[id];
    if(p){ clearTimeout(p.t); delete pendingDetach[id]; }
    detached[id] = 1; saveDet();
    decorate();
    var active = document.querySelector('.tab.active[data-view="' + id + '"]');
    if(active && window.__activateView) window.__activateView(firstAttachedTab(id));
    if(p && p.user) toast("Detached " + ((tabById(id) || {}).label || id) + " (close that window to put it back)");
  };

  /* Host (or the HTML solo window) calls this when a detached window closes. */
  window.__adcReattach = function(id){
    if(!detached[id]) return;
    delete detached[id]; saveDet();
    decorate();
    if(window.__activateView && tabById(id)) window.__activateView(id);
    toast("Re-attached " + ((tabById(id) || {}).label || id));
  };

  function decorate(){
    var host = document.getElementById("tabs");
    if(!host) return;
    host.querySelectorAll(".tab[data-view]").forEach(function(chip){
      var id = chip.getAttribute("data-view");
      var t = tabById(id);
      chip.classList.toggle("adc-detached", !!detached[id]);
      chip.classList.toggle("adc-solo-tab", !!SOLO && id === SOLO);
      if(t && t.group) chip.classList.add("adc-gtab");
      if(SOLO || chip.querySelector(".adc-tab-act")) return;
      var det = document.createElement("button");
      det.type = "button"; det.className = "adc-tab-act adc-tab-detach"; det.textContent = "\u21F1";
      det.title = "Detach this tab into its own window (or drag the tab down out of the strip)";
      det.addEventListener("click", function(e){ e.preventDefault(); e.stopPropagation(); detachTab(id); });
      chip.appendChild(det);
      if(t && t.group){
        var x = document.createElement("button");
        x.type = "button"; x.className = "adc-tab-act adc-tab-close"; x.textContent = "\u00D7";
        x.title = "Close this tab (its programs are closed, the group stays)";
        x.addEventListener("click", function(e){ e.preventDefault(); e.stopPropagation(); closeTab(id); });
        chip.appendChild(x);
        chip.addEventListener("auxclick", function(e){ if(e.button === 1){ e.preventDefault(); closeTab(id); } });
      }
      /* Drag a tab down out of the strip (or out of the window) to detach it. */
      chip.addEventListener("dragstart", function(e){
        if(e.defaultPrevented) return;
        document.body.classList.add("adc-dragging");
      });
      chip.addEventListener("dragend", function(e){
        document.body.classList.remove("adc-dragging");
        var r = host.getBoundingClientRect();
        var out = e.clientY > r.bottom + 60 || e.clientY < 0 || e.clientX < 0 || e.clientX > window.innerWidth || e.clientY > window.innerHeight
          || (e.screenX === 0 && e.screenY === 0 && e.clientX === 0 && e.clientY === 0);
        if(out) detachTab(id);
      });
    });
  }
  window.__adcDecorateTabs = decorate;

  /* Group header: middle-click or the small button opens the group as a tab. */
  window.__adcGroupTabButton = function(summary, group){
    var b = document.createElement("button");
    b.type = "button"; b.className = "adc-gtab-open"; b.textContent = "\u29C9";
    b.title = "Open " + (group.title || "this group") + " as a tab (or middle-click the group header)";
    b.draggable = false;
    b.addEventListener("click", function(e){ e.preventDefault(); e.stopPropagation(); openGroupAsTab(group.id); });
    summary.appendChild(b);
    summary.addEventListener("mousedown", function(e){ if(e.button === 1) e.preventDefault(); });
    summary.addEventListener("auxclick", function(e){
      if(e.button !== 1) return;
      if(e.target !== summary && e.target.closest && e.target.closest("details.lp-group > summary") !== summary) return;
      e.preventDefault(); e.stopPropagation(); openGroupAsTab(group.id);
    });
  };

  function boot(){
    var host = document.getElementById("tabs");
    var l = lp();
    if(!host || !l || !model()){ setTimeout(boot, 300); return; }
    new MutationObserver(function(){ decorate(); }).observe(host, { childList: true });
    if(SOLO){
      document.body.classList.add("adc-solo");
      document.title = ((tabById(SOLO) || {}).label || SOLO) + " - ADC";
      var goSolo = function(){
        if(!tabById(SOLO) && !(l.TAB_IDS.indexOf(SOLO) >= 0)) return;
        document.querySelectorAll(".tab[data-view]").forEach(function(t){ t.classList.toggle("active", t.getAttribute("data-view") === SOLO); });
        l.switchStage(SOLO);
      };
      document.addEventListener("cockpit:tab", function(e){ if(e.detail !== SOLO) setTimeout(goSolo, 0); });
      goSolo();
      if(!IS_WV2){
        window.addEventListener("beforeunload", function(){
          try { if(window.opener && window.opener.__adcReattach) window.opener.__adcReattach(SOLO); } catch(e){}
        });
      }
    } else if(IS_WV2){
      /* Detached tabs come back in their own windows after a restart. */
      var saved = loadDet();
      Object.keys(saved).forEach(function(id){
        if(!tabById(id)) return;
        detached[id] = 1;
      });
      saveDet();
      var again = Object.keys(detached);
      detached = {};
      again.forEach(function(id, i){
        setTimeout(function(){ requestDetach(id, (tabById(id) || {}).label || id, false); }, 1200 + i * 400);
      });
      // keep the list until the host answers (or does not)
      again.forEach(function(id){ try { var sv = loadDet(); sv[id] = 1; localStorage.setItem(DET_KEY, JSON.stringify(sv)); } catch(e){} });
    }
    decorate();
  }
  if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", function(){ setTimeout(boot, 0); });
  else setTimeout(boot, 0);
})();

/* 2026-10-09 group title rename (adc-grename). Click the title TEXT of a group header to rename it
   inline (Enter or leaving the box saves to groups.json, Esc cancels). The chevron and the rest of
   the header still collapse/expand; dragging the header still reorders (a drag never renames). */
(function(){
  var COLLAPSE_KEY = "lp-group-collapsed";
  function model(){ return window.__ADC && window.__ADC.model; }
  function toast(t){ var l = window.__ADC && window.__ADC.lp; if(l && l.toast) l.toast(t); }
  function startRename(summary, span, group){
    if(summary.querySelector("input.adc-grename")) return;
    var old = group.title || "";
    var input = document.createElement("input");
    input.type = "text";
    input.className = "adc-grename";
    input.value = old;
    input.setAttribute("aria-label", "Group name");
    input.size = Math.max(8, old.length + 2);
    var wasDraggable = summary.draggable;
    summary.draggable = false;
    span.style.display = "none";
    span.parentNode.insertBefore(input, span.nextSibling);
    window.__adcGroupRenaming = true;
    var done = false;
    function finish(save){
      if(done) return;
      done = true;
      window.__adcGroupRenaming = false;
      var name = String(input.value || "").replace(/\s+/g, " ").trim();
      input.remove();
      span.style.display = "";
      summary.draggable = wasDraggable;
      if(!save || !name || name === old) return;
      var m = model(); var g = null;
      if(m && Array.isArray(m.groups)) m.groups.forEach(function(x){ if(x && x.id === group.id) g = x; });
      if(!g) return;
      g.title = name;
      span.textContent = name;
      // keep the remembered collapsed state under the new name
      try {
        var st = JSON.parse(localStorage.getItem(COLLAPSE_KEY) || "{}") || {};
        if(Object.prototype.hasOwnProperty.call(st, old)){ st[name] = st[old]; delete st[old]; localStorage.setItem(COLLAPSE_KEY, JSON.stringify(st)); }
      } catch(e){}
      if(window.__adcCommit) window.__adcCommit({ tray: true });
      else if(window.__adcSaveOverlay) window.__adcSaveOverlay();
      toast("Renamed group to " + name);
    }
    ["click", "mousedown", "mouseup", "dblclick"].forEach(function(t){
      input.addEventListener(t, function(e){ e.stopPropagation(); if(t === "click") e.preventDefault(); });
    });
    input.addEventListener("keydown", function(e){
      e.stopPropagation();
      if(e.key === "Enter"){ e.preventDefault(); finish(true); }
      else if(e.key === "Escape"){ e.preventDefault(); finish(false); }
    });
    input.addEventListener("keyup", function(e){ e.stopPropagation(); e.preventDefault(); });
    input.addEventListener("blur", function(){ finish(true); });
    input.focus();
    input.select();
  }
  window.__adcGroupTitle = function(summary, group){
    var span = document.createElement("span");
    span.className = "adc-gtitle";
    span.textContent = group.title;
    span.title = "Click to rename";
    summary.appendChild(document.createTextNode(" "));
    summary.appendChild(span);
    summary.appendChild(document.createTextNode(" "));
    span.addEventListener("click", function(e){
      // a drag of the header never reaches here (the browser drops the click after a drag)
      e.preventDefault();
      e.stopPropagation();
      startRename(summary, span, group);
    });
  };
})();

/* 2026-10-09 tab label rename (adc-trename). Click the label of the ACTIVE tab to rename it inline
   (Enter or leaving the box saves to tabs.json, Esc cancels). Only the display label changes; the
   tab id and routing stay. Clicking an inactive tab (label included) still switches to it; clicking
   the rest of a tab or dragging it works as before. */
(function(){
  function model(){ return window.__ADC && window.__ADC.model; }
  function tabById(id){ var m = model(); return m && Array.isArray(m.tabs) ? m.tabs.filter(function(t){ return t && t.id === id; })[0] : null; }
  function toast(t){ var l = window.__ADC && window.__ADC.lp; if(l && l.toast) l.toast(t); }
  function startRename(chip, span){
    var id = chip.getAttribute("data-view");
    var tab = tabById(id);
    if(!tab || chip.querySelector("input.adc-trename")) return;
    var old = tab.label || id;
    var input = document.createElement("input");
    input.type = "text"; input.className = "adc-trename"; input.value = old;
    input.setAttribute("aria-label", "Tab name");
    input.size = Math.max(6, old.length + 1);
    var wasDrag = chip.getAttribute("draggable");
    chip.setAttribute("draggable", "false");
    span.style.display = "none";
    span.parentNode.insertBefore(input, span.nextSibling);
    var done = false;
    function finish(save){
      if(done) return; done = true;
      var name = String(input.value || "").replace(/\s+/g, " ").trim();
      input.remove(); span.style.display = "";
      if(wasDrag !== null) chip.setAttribute("draggable", wasDrag);
      if(!save || !name || name === old) return;
      tab.label = name;
      span.textContent = name;
      var lp = window.__ADC && window.__ADC.lp;
      if(lp && lp.TAB_LABELS) lp.TAB_LABELS[id] = name;
      if(window.__adcCommit) window.__adcCommit({ tabs: true });
      else if(window.__adcSaveOverlay) window.__adcSaveOverlay();
      toast("Renamed tab to " + name);
    }
    ["click", "mousedown", "mouseup", "dblclick", "dragstart"].forEach(function(t){
      input.addEventListener(t, function(e){ e.stopPropagation(); if(t === "click" || t === "dragstart") e.preventDefault(); });
    });
    input.addEventListener("keydown", function(e){
      e.stopPropagation();
      if(e.key === "Enter"){ e.preventDefault(); finish(true); }
      else if(e.key === "Escape"){ e.preventDefault(); finish(false); }
    });
    input.addEventListener("keyup", function(e){ e.stopPropagation(); });
    input.addEventListener("blur", function(){ finish(true); });
    input.focus(); input.select();
  }
  function wrap(){
    var host = document.getElementById("tabs");
    if(!host) return;
    host.querySelectorAll(".tab[data-view]").forEach(function(chip){
      if(chip.querySelector(":scope > .adc-tlabel")) return;
      var text = null;
      for(var n = chip.firstChild; n; n = n.nextSibling){ if(n.nodeType === 3 && n.nodeValue.trim()){ text = n; break; } }
      if(!text) return;
      var span = document.createElement("span");
      span.className = "adc-tlabel";
      span.textContent = text.nodeValue;
      chip.replaceChild(span, text);
      span.addEventListener("click", function(e){
        if(!chip.classList.contains("active")) return; // inactive: the click switches tabs as before
        e.preventDefault(); e.stopPropagation();
        startRename(chip, span);
      });
    });
  }
  window.__adcWrapTabLabels = wrap;
  function boot(){
    var host = document.getElementById("tabs");
    if(!host){ setTimeout(boot, 300); return; }
    new MutationObserver(wrap).observe(host, { childList: true });
    wrap();
  }
  if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", function(){ setTimeout(boot, 0); });
  else setTimeout(boot, 0);
})();

/* 2026-10-09 group header context menu (adc-gmenu). Right-click a group header (top-level or
   subgroup). Items live in window.__adcGroupMenuItems: { id, label, icon, enabled(group), action(group) };
   adding a menu item is one entry. Esc, an outside click, scrolling or resizing closes the menu;
   it is kept on screen near the edges. */
(function(){
  /* Delete group is offered only for an EMPTY group (no cards, no subgroups); otherwise it is grayed.
     Deleting a group that still has cards is not built (open question for Director). */
  var DELETE_ONLY_WHEN_EMPTY = true;
  function model(){ return window.__ADC && window.__ADC.model; }
  function lp(){ return window.__ADC && window.__ADC.lp; }
  function toast(t){ var l = lp(); if(l && l.toast) l.toast(t); }
  function counts(gid, cards, groups){
    return {
      cards: (cards || []).filter(function(c){ return c && c.groupId === gid; }).length,
      kids: (groups || []).filter(function(g){ return g && g.parentId === gid; }).length
    };
  }
  function isEmpty(group){
    var m = model(); if(!m) return false;
    var n = counts(group.id, m.cards, m.groups);
    return n.cards === 0 && n.kids === 0;
  }
  function fetchJson(name){
    return fetch("data/" + name + ".json?ts=" + Date.now(), { cache: "no-store" }).then(function(r){ return r.ok ? r.json() : null; }).catch(function(){ return null; });
  }
  function deleteGroup(group){
    var l = lp();
    var title = group.title || group.id;
    function doDelete(){
      // re-read groups.json and cards.json first: the group must still exist and still be empty
      /* 2026-10-09: the dialog shows "Deleting..." and closes when the delete finished or failed (adc-gmenu-close). */
      var done = false;
      function finish(){
        if(done) return; done = true;
        var l3 = lp();
        if(l3 && typeof l3.closeModal === "function") l3.closeModal();
        else { var me = document.getElementById("lpModal"); if(me) me.classList.remove("show"); }
      }
      try {
        var hintEl = document.querySelector("#lpModalHint");
        if(hintEl) hintEl.textContent = "Deleting " + title + "\u2026";
        document.querySelectorAll("#lpModal.show button").forEach(function(b){ b.disabled = true; });
      } catch(_){}
      setTimeout(function(){ if(!done){ toast("Deleting " + title + " is taking long; check the tray."); finish(); } }, 8000);
      Promise.all([fetchJson("groups"), fetchJson("cards")]).then(function(res){
        var fg = res[0] && Array.isArray(res[0].groups) ? res[0].groups : null;
        var fc = res[1] && Array.isArray(res[1].cards) ? res[1].cards : null;
        var m = model();
        if(!m || !fg || !fc){ toast("Could not re-read the saved groups; nothing deleted."); return; }
        if(!fg.some(function(g){ return g && g.id === group.id; })){
          m.groups = m.groups.filter(function(g){ return g && g.id !== group.id; });
          if(window.__adcCommit) window.__adcCommit({ tray: true });
          toast(title + " was already deleted.");
          return;
        }
        var n = counts(group.id, fc, fg), nm = counts(group.id, m.cards, m.groups);
        if(DELETE_ONLY_WHEN_EMPTY && (n.cards || n.kids || nm.cards || nm.kids)){
          toast(title + " is not empty any more; nothing deleted.");
          return;
        }
        m.groups = m.groups.filter(function(g){ return g && g.id !== group.id; });
        if(window.__adcCommit) window.__adcCommit({ tray: true });
        else if(window.__adcSaveOverlay) window.__adcSaveOverlay();
        toast("Deleted group " + title);
      }).catch(function(err){
        toast("Delete failed: " + ((err && err.message) || "unknown error") + ". Nothing was deleted.");
      }).then(function(){ finish(); });
      return true; // keep the dialog open (Deleting...) until the delete finished, then finish() closes it
    }
    if(l && l.openModal){
      l.openModal({
        title: "Delete group",
        hint: "Delete the empty group \u201C" + title + "\u201D? This removes it from groups.json.",
        actions: [
          { label: "Cancel", onClick: function(){ /* closes the dialog */ } },
          { label: "Delete", primary: true, onClick: doDelete }
        ]
      });
    } else if(window.confirm("Delete the empty group \u201C" + title + "\u201D?")) doDelete();
  }
  var ITEMS = window.__adcGroupMenuItems = window.__adcGroupMenuItems || [];
  /* 2026-10-09: group actions work without edit mode; they call the existing editor functions. */
  function act(name){ return function(g){ var a = window.__adcGroupActions; if(a && a[name]) a[name](g); else toast("Not ready yet; try again in a moment."); }; }
  function ready(name){ return function(){ return !!(window.__adcGroupActions && window.__adcGroupActions[name]); }; }
  ITEMS.push({ id: "edit-group", label: "Edit group", icon: "\u270E", enabled: ready("edit"), action: act("edit") });
  ITEMS.push({ id: "add-card", label: "Add card", icon: "\uFF0B", enabled: ready("addCard"), action: act("addCard") });
  ITEMS.push({ id: "add-subgroup", label: "Add subgroup", icon: "\u2295", enabled: ready("addSubgroup"), action: act("addSubgroup") });
  ITEMS.push({
    id: "delete-group",
    label: "Delete group",
    icon: "\uD83D\uDDD1",
    title: function(g){ return isEmpty(g) ? "Delete this empty group" : "Only an empty group can be deleted (move its cards and subgroups out first)"; },
    enabled: function(g){ return DELETE_ONLY_WHEN_EMPTY ? isEmpty(g) : true; },
    action: deleteGroup
  });

  var menu = null;
  function close(){
    if(!menu) return;
    menu.remove(); menu = null;
    document.removeEventListener("mousedown", outside, true);
    document.removeEventListener("keydown", onKey, true);
    window.removeEventListener("resize", close);
    window.removeEventListener("blur", close);
    document.removeEventListener("scroll", close, true);
  }
  function outside(e){ if(menu && !menu.contains(e.target)) close(); }
  function onKey(e){ if(e.key === "Escape"){ e.preventDefault(); e.stopPropagation(); close(); } }
  function open(group, x, y){
    close();
    menu = document.createElement("div");
    menu.className = "adc-ctx open";
    menu.setAttribute("role", "menu");
    var head = document.createElement("div");
    head.className = "adc-ctx-head";
    head.textContent = group.title || group.id;
    menu.appendChild(head);
    ITEMS.forEach(function(item){
      var ok = true;
      try { ok = typeof item.enabled === "function" ? !!item.enabled(group) : true; } catch(e){ ok = false; }
      var b = document.createElement("button");
      b.type = "button";
      b.className = "adc-ctx-item" + (ok ? "" : " disabled");
      b.setAttribute("role", "menuitem");
      b.dataset.item = item.id || "";
      if(!ok) b.setAttribute("aria-disabled", "true");
      try { var t = typeof item.title === "function" ? item.title(group) : item.title; if(t) b.title = t; } catch(e){}
      var ic = document.createElement("span"); ic.className = "adc-ctx-ico"; ic.textContent = item.icon || "";
      b.appendChild(ic);
      b.appendChild(document.createTextNode(item.label));
      b.addEventListener("click", function(e){
        e.preventDefault(); e.stopPropagation();
        if(!ok) return;
        close();
        try { item.action(group); } catch(err){ toast("That did not work: " + err.message); }
      });
      menu.appendChild(b);
    });
    document.body.appendChild(menu);
    var r = menu.getBoundingClientRect();
    var left = Math.min(x, window.innerWidth - r.width - 6), top = Math.min(y, window.innerHeight - r.height - 6);
    menu.style.left = Math.max(6, left) + "px";
    menu.style.top = Math.max(6, top) + "px";
    setTimeout(function(){
      document.addEventListener("mousedown", outside, true);
      document.addEventListener("keydown", onKey, true);
      window.addEventListener("resize", close);
      window.addEventListener("blur", close);
      document.addEventListener("scroll", close, true);
    }, 0);
  }
  window.__adcOpenGroupMenu = open;
  document.addEventListener("contextmenu", function(e){
    var summary = e.target && e.target.closest && e.target.closest("details.lp-group > summary");
    if(!summary || (e.target.closest && e.target.closest("input"))) return;
    var gid = summary.parentNode.getAttribute("data-group-id");
    var m = model(); if(!m || !Array.isArray(m.groups)) return;
    var group = m.groups.filter(function(g){ return g && g.id === gid; })[0];
    if(!group) return;
    e.preventDefault();
    open(group, e.clientX, e.clientY);
  });
})();

/* 2026-10-09 multi-window (adc-newwin). WV2 only: a "New window" button in the title row and Ctrl+Shift+N
   open another cockpit window in the same cockpit process (for another monitor). */
(function(){
  var host = window.chrome && window.chrome.webview;
  if(!host || window.__adcSoloTab) return;
  function openNew(){ try { host.postMessage({ type: 'adc-new-window' }); } catch(_){} }
  function addButton(){
    if(document.querySelector('.adc-newwin')) return true;
    var tools = document.querySelector('header .adc-head-tools') || document.querySelector('header');
    if(!tools) return false;
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'adc-newwin';
    b.textContent = '\u29C9 New window';
    b.title = 'Open another cockpit window, e.g. for another monitor (Ctrl+Shift+N)';
    b.addEventListener('click', openNew);
    tools.insertBefore(b, tools.firstChild);
    return true;
  }
  document.addEventListener('keydown', function(e){
    if(e.ctrlKey && e.shiftKey && !e.altKey && (e.key === 'N' || e.key === 'n')){ e.preventDefault(); openNew(); }
  }, true);
  if(!addButton()){
    var tries = 0, t = setInterval(function(){ if(addButton() || ++tries > 40) clearInterval(t); }, 250);
  }
})();

/* 2026-10-09 build version label (adc-version). WV2 only: shows the host build (window.__adcHostVersion, set by
   the host before this page loads) in the title row, and warns when this page js was written for another host
   version (PAGE_VERSION must equal the csproj VersionPrefix; build stamps differ normally because page fixes go live). */
(function(){
  var PAGE_VERSION = '3.0.0';
  window.__adcPageVersion = PAGE_VERSION;
  var host = window.chrome && window.chrome.webview;
  if(!host) return;
  function render(){
    var tools = document.querySelector('header .adc-head-tools') || document.querySelector('header');
    if(!tools) return false;
    if(document.querySelector('.adc-ver')) return true;
    var hv = window.__adcHostVersion, el = document.createElement('span');
    el.className = 'adc-ver';
    el.style.cssText = 'font-size:11px;opacity:.75;margin:0 8px;white-space:nowrap;align-self:center;';
    var warn = '';
    if(!hv || !hv.version){
      el.textContent = 'host: old build (no version)';
      warn = 'This cockpit build predates version reporting (page js v' + PAGE_VERSION + '). Reopen the cockpit to install the staged build.';
    } else {
      el.textContent = hv.display;
      var core = String(hv.version).split('+')[0];
      el.title = 'Host ' + hv.display + ' / page js v' + PAGE_VERSION;
      if(core !== PAGE_VERSION){
        el.textContent = hv.display + ' \u2260 page v' + PAGE_VERSION;
        warn = 'Page/host mismatch: page js is v' + PAGE_VERSION + ' but the host is ' + hv.display + '. Reopen the cockpit (or republish the page) so both match.';
      }
    }
    if(!warn) return true; /* 2026-10-09 10:21 PM: no label when versions match; the version is in the window title only (adc-ver-warn-only) */
    if(warn){
      el.title = warn;
      el.style.cssText += 'opacity:1;color:#1b0b00;background:#ffb020;border-radius:4px;padding:1px 6px;';
      el.textContent = '\u26a0 ' + el.textContent;
    }
    tools.appendChild(el);
    return true;
  }
  if(!render()){
    var tries = 0, t = setInterval(function(){ if(render() || ++tries > 40) clearInterval(t); }, 250);
  }
})();

/* 2026-10-09 v1.5.0 cockpit redesign (adc-setups). Spec: adc-redesign-mockup-20261009 (v2, single row).
   - ONE tab row of saved SETUPS (layout + panes), drawn only from saved data (tabs.json). '+' adds one,
     drag reorders, hover shows grip and the detach icon, double-click renames, right-click for more.
     Each setup's stage is auto-saved into its tabs.json record (tab.stage); Ctrl+S saves now.
   - The hardwired agent tabs (Launch Pad, Main / Conductor, Caddy, Runway, Sterling, Phoenix, DevOps,
     BallsLeader, Full Linear) are gone; a one-time migration removes them from tabs.json, turns the old
     Launch Pad stage into the setup "Main", and turns saved configs (lp-configs) into setups. Everything
     it touched is first copied to localStorage adc-backup-v150.
   - Edit/admin mode is retired: every edit lives in right-click menus (one shared menu: window.__adcMenu).
   Internal ids stay ('launch' is the storage id of the first setup); nothing routes on labels. */
(function(){
  if(window.__adcSetups) return;
  window.__adcSetups = 1;
  var IS_WV2 = !!(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function");
  var AGENT_TABS = ["main", "caddy", "runway", "sterling", "phoenix", "devops", "balls", "linear"];
  var MIGRATED_KEY = "adc-setups-v150";
  var NATIVE = "@native";
  function M(){ return window.__ADC && window.__ADC.model; }
  function L(){ return window.__ADC && window.__ADC.lp; }
  function A(){ return window.__adcActions || {}; }
  function toast(t){ var l = L(); if(l && l.toast) l.toast(t); }
  function solo(){ return window.__adcSoloTab || ""; }
  function isMainWindow(){ var k = window.__adcWindowKey; return !solo() && (!k || k === "main"); }
  function tabs(){ var m = M(); return m && Array.isArray(m.tabs) ? m.tabs : []; }
  function tabById(id){ return tabs().filter(function(t){ return t && t.id === id; })[0] || null; }
  function byOrder(a, b){ return (a.order || 0) - (b.order || 0); }
  function visibleTabs(){ return tabs().filter(function(t){ return t && !t.hidden; }).sort(byOrder); }
  function activeId(){ var c = document.querySelector("#tabs .tab.active[data-view]"); return c ? c.getAttribute("data-view") : ""; }
  function nextOrder(){ var n = 0; tabs().forEach(function(t){ var o = parseInt(t && t.order, 10); if(isFinite(o) && o > n) n = o; }); return n + 1; }
  function readLS(k){ try { return JSON.parse(localStorage.getItem(k) || "null"); } catch(e){ return null; } }
  function commitTabs(){ if(window.__adcCommit) window.__adcCommit({ tabs: true }); }
  function layoutLabel(id){
    var m = M(); var ls = m && m.stages && Array.isArray(m.stages.layouts) ? m.stages.layouts : [];
    var f = ls.filter(function(x){ return x && x.id === id; })[0];
    return f ? (f.label || f.id) : (id || "");
  }
  function mintTabId(label){
    var stem = "s-" + (String(label || "setup").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 30) || "setup");
    var id = stem, n = 2;
    while(tabById(id) || document.getElementById("view-" + id)){ id = stem + "-" + n; n++; }
    return id;
  }
  function cardForApp(app){
    var m = M(); if(!m || !app) return null;
    return (m.cards || []).filter(function(c){ return c && c.app === app; })[0] || null;
  }
  function isWeb(url){ return /^https?:\/\//i.test(String(url || "")); }
  function snap(id){
    var l = L(); var st = l && l.stages && l.stages[id]; if(!st) return null;
    return {
      layout: st.layout,
      slots: (st.slots || []).map(function(a){ return a === NATIVE ? null : (a || null); }),
      sizes: st.sizes && typeof st.sizes === "object" ? JSON.parse(JSON.stringify(st.sizes)) : {},
      loads: (st.loads || []).map(function(x){ return x && typeof x === "object" ? { cardId: x.cardId || "", url: x.url || "" } : null; })
    };
  }
  function putStage(id, s){
    var l = L(); if(!l || !s) return;
    var layout = l.LAYOUTS[s.layout] ? s.layout : "single";
    var slots = (s.slots || []).slice();
    var need = (l.LAYOUTS[layout] && l.LAYOUTS[layout].count) || 1;
    while(slots.length < need) slots.push(null);
    var loads = (s.loads || []).slice(0, slots.length);
    while(loads.length < slots.length) loads.push(null);
    if(l.TAB_IDS.indexOf(id) < 0) l.TAB_IDS.push(id);
    var t = tabById(id); if(t) l.TAB_LABELS[id] = t.label || id;
    l.ensureTabStage(id);
    l.stages[id] = { layout: layout, slots: slots, sizes: s.sizes || {}, loads: loads, activeSlot: null, drag: null };
  }

  /* ---------- one-time migration ---------- */
  function migrate(){
    var m = M(), l = L();
    if(!m || !l || !isMainWindow()) return false;
    try { if(localStorage.getItem(MIGRATED_KEY)) return false; } catch(e){ return false; }
    try {
      localStorage.setItem("adc-backup-v150", JSON.stringify({
        at: new Date().toISOString(), tabs: m.tabs, configs: readLS("lp-configs"), configGroups: readLS("lp-config-groups"),
        tabStages: readLS("lp-tab-stages"), activeTab: localStorage.getItem("lp-active-tab"), detached: readLS("adc-detached-tabs")
      }));
    } catch(e){}
    var removed = 0, added = 0;
    var before = tabs().length;
    if(!Array.isArray(m.tabs)) m.tabs = [];
    for(var i = m.tabs.length - 1; i >= 0; i--){ var x = m.tabs[i]; if(!x || AGENT_TABS.indexOf(x.id) >= 0) m.tabs.splice(i, 1); } // in place: other code holds this array
    removed = before - m.tabs.length;
    AGENT_TABS.forEach(function(id){ try { if(l.removeTabStage) l.removeTabStage(id); } catch(e){} });
    var main = tabById("launch");
    if(!main && !tabs().length){ main = { id: "launch", label: "Main", order: 1, mark: "" }; m.tabs.push(main); }
    if(main){ if(!main.label || /launch pad/i.test(main.label)) main.label = "Main"; delete main.extraClass; }
    (readLS("lp-configs") || []).forEach(function(cfg){
      if(!cfg || !cfg.name || !Array.isArray(cfg.slots)) return;
      var name = String(cfg.name).trim(); if(!name) return;
      if(tabs().some(function(t){ return String(t.label || "").toLowerCase() === name.toLowerCase(); })) return;
      var layout = l.LAYOUTS[cfg.layout] ? cfg.layout : "half";
      var slots = cfg.slots.map(function(a){ return a === NATIVE ? null : (a || null); });
      var loads = slots.map(function(a){ var c = cardForApp(a); return c ? { cardId: c.id, url: c.url || "" } : null; });
      var sizes = {}; if(cfg.sizes && Array.isArray(cfg.sizes.cols)) sizes[layout] = cfg.sizes;
      var id = mintTabId(name);
      m.tabs.push({ id: id, label: name, order: nextOrder(), mark: "", fromConfig: cfg.id || "" });
      putStage(id, { layout: layout, slots: slots, sizes: sizes, loads: loads });
      added++;
    });
    tabs().forEach(function(t){ t.setup = true; var s = snap(t.id); if(s) t.stage = s; });
    if(l.persistStages) l.persistStages();
    var act = activeId();
    commitTabs();
    if(!tabById(act) || AGENT_TABS.indexOf(act) >= 0){
      var first = visibleTabs()[0]; if(first && window.__activateView) window.__activateView(first.id);
    }
    try { localStorage.setItem(MIGRATED_KEY, new Date().toISOString()); } catch(e){}
    toast("Cockpit v1.5: " + tabs().length + " setup tabs (" + added + " from saved configs, " + removed + " old agent tabs removed). Backup kept.");
    return true;
  }

  /* A window whose own page storage has no stage for a setup uses the saved one from tabs.json. */
  function adoptSavedStages(){
    var l = L(); if(!l) return;
    var local = readLS("lp-tab-stages") || {};
    var act = activeId(), redraw = false;
    tabs().forEach(function(t){
      if(!t || !t.stage || local[t.id]) return;
      putStage(t.id, t.stage);
      if(t.id === act) redraw = true;
    });
    if(redraw && l.switchStage) l.switchStage(act);
  }

  /* ---------- auto-save ---------- */
  var lastSaved = {};
  function stateEl(){ return document.getElementById("adcSaveState"); }
  function showSaved(when){
    var el = stateEl(); if(!el) return;
    var d = when || new Date();
    el.innerHTML = "";
    var b = document.createElement("b"); b.textContent = "Saved \u2713";
    el.appendChild(b);
    el.appendChild(document.createTextNode(" " + d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })));
  }
  function busy(){ var lv = window.__adcLive; try { return !!(lv && lv.busy && lv.busy()); } catch(e){ return false; } }
  function saveSetup(id, force){
    var t = tabById(id); if(!t) return false;
    var s = snap(id); if(!s) return false;
    var js = JSON.stringify(s);
    if(!force && (lastSaved[id] === js || JSON.stringify(t.stage || null) === js)){ lastSaved[id] = js; return false; }
    t.stage = s; t.setup = true; lastSaved[id] = js;
    if(window.__adcSaveOverlay) window.__adcSaveOverlay();
    showSaved();
    return true;
  }
  function autoSave(){
    if(busy()) return;
    var id = solo() || activeId();
    if(id) saveSetup(id, false);
  }
  setInterval(autoSave, 2500);

  /* ---------- setup actions ---------- */
  function askName(title, hint, value, onOk){
    var l = L(); if(!l || !l.openModal){ var v = window.prompt(title, value || ""); if(v && v.trim()) onOk(v.trim()); return; }
    var input = null;
    l.openModal({
      title: title, hint: hint,
      buildBody: function(body){
        input = document.createElement("input"); input.type = "text"; input.className = "adc-name-input";
        input.value = value || ""; input.style.width = "100%";
        input.addEventListener("keydown", function(e){ if(e.key === "Enter"){ e.preventDefault(); var b = document.querySelector("#lpModalActions button.primary"); if(b) b.click(); } });
        body.appendChild(input);
        setTimeout(function(){ try { input.focus(); input.select(); } catch(e){} }, 30);
      },
      actions: [
        { label: "Cancel" },
        { label: "Create", primary: true, onClick: function(){ var v = input && input.value.trim(); if(!v){ toast("Type a name first"); return true; } onOk(v); } }
      ]
    });
  }
  function startInlineRename(id){
    setTimeout(function(){
      var chip = document.querySelector('#tabs .tab[data-view="' + id + '"]');
      var lab = chip && chip.querySelector(".adc-tlabel");
      if(lab && chip.classList.contains("active")) lab.click();
    }, 120);
  }
  function createSetup(label, stage){
    var m = M(), l = L(); if(!m || !l) return null;
    var id = mintTabId(label);
    var t = { id: id, label: label, order: nextOrder(), mark: "", setup: true };
    m.tabs.push(t);
    putStage(id, stage);
    t.stage = snap(id);
    if(l.persistStages) l.persistStages();
    commitTabs();
    if(window.__activateView) window.__activateView(id);
    return id;
  }
  function newSetup(){
    askName("New setup tab", "A new empty setup with the current layout. It is added at the end of the tab row.", "", function(name){
      var l = L(); var cur = l && l.stages[activeId()];
      var layout = cur ? cur.layout : "half";
      var n = (l.LAYOUTS[layout] && l.LAYOUTS[layout].count) || 1, slots = [];
      for(var i = 0; i < n; i++) slots.push(null);
      var id = createSetup(name, { layout: layout, slots: slots, sizes: cur && cur.sizes ? JSON.parse(JSON.stringify(cur.sizes)) : {}, loads: [] });
      if(id){ toast("Created setup " + name); startInlineRename(id); }
    });
  }
  function duplicateSetup(id, asNew){
    var t = tabById(id); var s = snap(id); if(!t || !s) return;
    function make(name){
      // web pages are copied; desktop programs stay in the original setup (one program, one pane)
      var loads = s.loads.map(function(x){ return x && isWeb(x.url) ? x : null; });
      var slots = s.slots.map(function(a, i){ return loads[i] ? a : null; });
      var nid = createSetup(name, { layout: s.layout, slots: slots, sizes: s.sizes, loads: loads });
      if(nid) toast("Created " + name);
    }
    if(asNew) askName("Save as new setup", "Saves this layout and its web panes as a new setup tab.", (t.label || id) + " copy", make);
    else make((t.label || id) + " copy");
  }
  function hasPrograms(id){
    var s = snap(id); if(!s) return false;
    return s.loads.some(function(x){ return x && x.url && !isWeb(x.url); });
  }
  function closeSetup(id){
    var t = tabById(id); if(!t) return;
    var vis = visibleTabs();
    if(vis.length <= 1){ toast("Keep at least one setup tab"); return; }
    saveSetup(id, false);
    if(activeId() === id){ var next = vis.filter(function(x){ return x.id !== id; })[0]; if(next && window.__activateView) window.__activateView(next.id); }
    t.hidden = true;
    commitTabs();
    toast("Closed " + (t.label || id) + " (programs keep running; reopen it from the tab row's right-click menu)");
  }
  function reopenSetup(id){
    var t = tabById(id); if(!t) return;
    delete t.hidden; t.order = nextOrder();
    commitTabs();
    if(window.__activateView) window.__activateView(id);
  }
  function deleteSetup(id){
    var t = tabById(id); if(!t) return;
    if(visibleTabs().length <= 1 && !t.hidden){ toast("Keep at least one setup tab"); return; }
    var l = L(); var label = t.label || id;
    var progs = hasPrograms(id);
    l.openModal({
      title: "Delete setup",
      hint: "Delete the setup \u201C" + label + "\u201D? Its layout and panes are removed from tabs.json" + (progs ? ", and the desktop programs open in it are closed." : ".") + " Cards are not touched.",
      actions: [
        { label: "Cancel" },
        { label: "Delete", primary: true, onClick: function(){
          if(activeId() === id){ var next = visibleTabs().filter(function(x){ return x.id !== id; })[0]; if(next && window.__activateView) window.__activateView(next.id); }
          if(window.__adcCloseTab) window.__adcCloseTab(id);
          toast("Deleted setup " + label);
        } }
      ]
    });
  }
  function renameSetup(id){
    if(window.__activateView && activeId() !== id) window.__activateView(id);
    startInlineRename(id);
  }
  function detachSetup(id){
    if(window.__adcDetachTab) window.__adcDetachTab(id);
  }

  /* ---------- shared right-click menu (window.__adcMenu) ---------- */
  var menuEl = null, subEl = null;
  function closeMenu(){
    if(subEl){ subEl.remove(); subEl = null; }
    if(!menuEl) return;
    menuEl.remove(); menuEl = null;
    document.removeEventListener("mousedown", outside, true);
    document.removeEventListener("keydown", onKey, true);
    window.removeEventListener("resize", closeMenu);
    window.removeEventListener("blur", closeMenu);
    document.removeEventListener("scroll", onScroll, true);
  }
  function outside(e){ if((menuEl && menuEl.contains(e.target)) || (subEl && subEl.contains(e.target))) return; closeMenu(); }
  function onKey(e){ if(e.key === "Escape"){ e.preventDefault(); e.stopPropagation(); closeMenu(); } }
  function onScroll(e){ if((menuEl && menuEl.contains(e.target)) || (subEl && subEl.contains(e.target))) return; closeMenu(); }
  function place(el, x, y){
    var r = el.getBoundingClientRect();
    el.style.left = Math.max(6, Math.min(x, window.innerWidth - r.width - 6)) + "px";
    el.style.top = Math.max(6, Math.min(y, window.innerHeight - r.height - 6)) + "px";
  }
  function build(caption, items, isSub){
    var el = document.createElement("div");
    el.className = "adc-ctx adc-cm open" + (isSub ? " adc-cm-sub" : "");
    el.setAttribute("role", "menu");
    if(caption){ var c = document.createElement("div"); c.className = "adc-ctx-head"; c.textContent = caption; el.appendChild(c); }
    items.forEach(function(it){
      if(!it) return;
      if(it === "-"){ var s = document.createElement("div"); s.className = "adc-cm-sep"; el.appendChild(s); return; }
      var ok = it.disabled ? false : true;
      var b = document.createElement("button");
      b.type = "button";
      b.className = "adc-ctx-item" + (ok ? "" : " disabled") + (it.danger ? " danger" : "") + (it.sub ? " has-sub" : "");
      b.setAttribute("role", "menuitem");
      if(it.id) b.dataset.item = it.id;
      if(!ok) b.setAttribute("aria-disabled", "true");
      if(it.title) b.title = it.title;
      var ic = document.createElement("span"); ic.className = "adc-ctx-ico"; ic.textContent = it.icon || ""; b.appendChild(ic);
      var lb = document.createElement("span"); lb.className = "adc-cm-lbl"; lb.textContent = it.label; b.appendChild(lb);
      if(it.sc || it.sub){ var sc = document.createElement("span"); sc.className = "adc-cm-sc"; sc.textContent = it.sub ? "\u25B8" : it.sc; b.appendChild(sc); }
      function openSub(){
        if(subEl){ subEl.remove(); subEl = null; }
        var list = typeof it.sub === "function" ? it.sub() : it.sub;
        if(!list || !list.length) list = [{ label: "(none)", disabled: true }];
        subEl = build("", list, true);
        document.body.appendChild(subEl);
        var r = b.getBoundingClientRect();
        var w = subEl.getBoundingClientRect().width;
        var x = r.right + 2; if(x + w > window.innerWidth - 6) x = r.left - w - 2;
        place(subEl, x, r.top - 4);
      }
      if(it.sub){
        b.addEventListener("mouseenter", function(){ if(ok) openSub(); });
        b.addEventListener("click", function(e){ e.preventDefault(); e.stopPropagation(); if(ok) openSub(); });
      } else {
        b.addEventListener("mouseenter", function(){ if(!isSub && subEl){ subEl.remove(); subEl = null; } });
        b.addEventListener("click", function(e){
          e.preventDefault(); e.stopPropagation();
          if(!ok) return;
          closeMenu();
          try { it.action(); } catch(err){ toast("That did not work: " + err.message); }
        });
      }
      el.appendChild(b);
    });
    return el;
  }
  function openMenu(caption, items, x, y){
    closeMenu();
    menuEl = build(caption, items, false);
    document.body.appendChild(menuEl);
    place(menuEl, x, y);
    setTimeout(function(){
      document.addEventListener("mousedown", outside, true);
      document.addEventListener("keydown", onKey, true);
      window.addEventListener("resize", closeMenu);
      window.addEventListener("blur", closeMenu);
      document.addEventListener("scroll", onScroll, true);
    }, 0);
  }
  var REG = {};
  window.__adcMenu = { open: openMenu, close: closeMenu, register: function(kind, fn){ REG[kind] = fn; }, kinds: REG };

  /* ---------- menu contents ---------- */
  function hiddenTabsSub(){
    return tabs().filter(function(t){ return t && t.hidden; }).sort(byOrder).map(function(t){
      return { label: t.label || t.id, icon: "\u21A9", action: function(){ reopenSetup(t.id); } };
    });
  }
  REG.tab = function(ctx){
    var id = ctx.id, t = tabById(id); if(!t) return null;
    return { caption: "Setup tab \u00B7 " + (t.label || id), items: [
      { icon: "\u270E", label: "Rename", sc: "F2", action: function(){ renameSetup(id); } },
      { icon: "\uD83D\uDCBE", label: "Save", sc: "Ctrl+S", action: function(){ saveSetup(id, true); toast("Saved " + (t.label || id)); } },
      { icon: "\u2795", label: "Save as new\u2026", action: function(){ duplicateSetup(id, true); } },
      { icon: "\u29C9", label: "Duplicate", action: function(){ duplicateSetup(id, false); } },
      "-",
      { icon: "\uD83D\uDDA5", label: "Detach to new window", disabled: !!solo(), action: function(){ detachSetup(id); } },
      "-",
      { icon: "\u2715", label: "Close tab", sc: "Ctrl+W", disabled: visibleTabs().length <= 1, action: function(){ closeSetup(id); } },
      { icon: "\uD83D\uDDD1", label: "Delete setup\u2026", danger: true, disabled: visibleTabs().length <= 1 && !t.hidden, action: function(){ deleteSetup(id); } },
      "-",
      { icon: "\uFF0B", label: "New setup tab\u2026", action: newSetup },
      { icon: "\u21A9", label: "Reopen closed setup", sub: hiddenTabsSub },
      { icon: "\u2630", label: "All tabs list\u2026", sc: "Ctrl+Shift+A", disabled: !A().tabList, action: function(){ A().tabList(); } },
      { icon: "\u270E", label: "Edit tabs\u2026", disabled: !A().editTab, action: function(){ A().editTab(t); } }
    ] };
  };
  REG.tabrow = function(){
    return { caption: "Setup tabs", items: [
      { icon: "\uFF0B", label: "New setup tab\u2026", action: newSetup },
      { icon: "\u21A9", label: "Reopen closed setup", sub: hiddenTabsSub },
      { icon: "\u2630", label: "All tabs list\u2026", sc: "Ctrl+Shift+A", disabled: !A().tabList, action: function(){ A().tabList(); } }
    ] };
  };
  function groupsSub(card){
    var m = M(); if(!m) return [];
    return (m.groups || []).slice().sort(byOrder).map(function(g){
      return { label: (g.parentId ? "\u2003" : "") + (g.title || g.id), disabled: g.id === card.groupId, action: function(){
        var max = 0; (m.cards || []).forEach(function(c){ if(c && c.groupId === g.id && (c.order || 0) > max) max = c.order || 0; });
        card.groupId = g.id; card.order = max + 1;
        if(A().commit) A().commit({ tray: true });
        toast("Moved " + (card.name || card.app) + " to " + (g.title || g.id));
      } };
    });
  }
  function slotOfCard(card){
    var l = L(); var st = l && l.stages[activeId()]; if(!st || !st.loads) return -1;
    for(var i = 0; i < st.loads.length; i++){ var x = st.loads[i]; if(x && x.cardId === card.id) return i; }
    for(var j = 0; j < (st.slots || []).length; j++){ if(st.slots[j] === card.app) return j; }
    return -1;
  }
  function clickIn(sel){ var b = document.querySelector(sel); if(b){ b.click(); return true; } return false; }
  REG.card = function(ctx){
    var tile = ctx.el, m = M(); if(!m) return null;
    var card = (m.cards || []).filter(function(c){ return c && c.id === tile.getAttribute("data-card-id"); })[0];
    if(!card) return null;
    return { caption: "Card \u00B7 " + (card.name || card.app), items: [
      { icon: "\u25A3", label: "Open in active pane", sc: "Click", action: function(){ window.__adcSuppressCardClick = false; delete tile.dataset.adcSuppressClick; tile.click(); } },
      { icon: "\u229E", label: "Open in new pane", action: function(){ var l = L(); if(l && l.addPane) l.addPane(); window.__adcSuppressCardClick = false; delete tile.dataset.adcSuppressClick; tile.click(); } },
      "-",
      { icon: "\u270E", label: "Edit card\u2026", disabled: !A().editCard, action: function(){ A().editCard(card); } },
      { icon: "\u29C9", label: "Copy card", disabled: !A().copyCard, action: function(){
        if(A().copyCard(card)) toast("Copied " + (card.name || card.app));
        else toast("Could not copy " + (card.name || card.app));
      } },
      { icon: "\u279C", label: "Move to group", sub: function(){ return groupsSub(card); } },
      { icon: "\uD83D\uDD11", label: "Login settings\u2026", action: function(){
        var i = slotOfCard(card);
        if(i < 0 || !clickIn('.lp-pane-h [data-login-slot="' + i + '"]')) toast("Open " + (card.name || card.app) + " in a pane first, then use Login settings");
      } },
      "-",
      { icon: "\uD83D\uDDD1", label: "Delete card\u2026", danger: true, disabled: !A().deleteCard, action: function(){ A().deleteCard(card); } }
    ] };
  };
  REG.pane = function(ctx){
    var i = parseInt(ctx.el.getAttribute("data-slot-drag"), 10);
    var l = L(); var st = l && l.stages[activeId()];
    if(!st || isNaN(i)) return null;
    var name = (ctx.el.querySelector(".app-name") || {}).textContent || ("Pane " + (i + 1));
    var vault = !!ctx.el.querySelector("[data-fill-slot]");
    var moveSub = function(){
      var out = [];
      for(var j = 0; j < st.slots.length; j++){
        if(j === i) continue;
        (function(j){ out.push({ label: "Pane " + (j + 1) + (st.slots[j] ? " \u00B7 " + (st.slots[j] === NATIVE ? "tab view" : st.slots[j]) : " (empty)"), action: function(){ if(l.movePane) l.movePane(i, j); } }); })(j);
      }
      return out;
    };
    return { caption: "Pane \u00B7 " + name.trim(), items: [
      { icon: "\uD83D\uDD11", label: "Fill login", sc: "Ctrl+Shift+L", disabled: !vault, action: function(){ clickIn('.lp-pane-h [data-fill-slot="' + i + '"]'); } },
      { icon: "\uD83D\uDC64", label: "Copy username", disabled: !vault, action: function(){ clickIn('.lp-pane-h [data-copy-slot="' + i + '"][data-copy-field="u"]'); } },
      { icon: "\u2022\u2022\u2022", label: "Copy password", sc: "clears in 30s", disabled: !vault, action: function(){ clickIn('.lp-pane-h [data-copy-slot="' + i + '"][data-copy-field="p"]'); } },
      { icon: "\u2699", label: "Login settings\u2026", disabled: !vault, action: function(){ clickIn('.lp-pane-h [data-login-slot="' + i + '"]'); } },
      "-",
      { icon: "\u21C4", label: "Move to pane", sub: moveSub },
      { icon: "\u29C9", label: "Detach", disabled: !ctx.el.querySelector("[data-detach-slot]"), action: function(){ clickIn('.lp-pane-h [data-detach-slot="' + i + '"]'); } },
      { icon: "\u27F3", label: "Reload", action: function(){
        if(IS_WV2){ try { window.chrome.webview.postMessage({ type: "adc-reload-pane", space: activeId(), slot: i }); } catch(e){} return; }
        var f = document.querySelector('.lp-slot[data-slot="' + i + '"] iframe'); try { if(f) f.contentWindow.location.reload(); } catch(e){ if(f) f.src = f.src; }
      } },
      "-",
      { icon: "\u2715", label: "Close pane", danger: true, action: function(){ clickIn('.lp-pane-h [data-close-slot="' + i + '"]'); } }
    ] };
  };
  REG.group = function(ctx){
    var m = M(); if(!m) return null;
    var gid = ctx.el.parentNode.getAttribute("data-group-id");
    var group = (m.groups || []).filter(function(g){ return g && g.id === gid; })[0];
    if(!group) return null;
    var items = [];
    (window.__adcGroupMenuItems || []).forEach(function(it){
      var ok = true; try { ok = typeof it.enabled === "function" ? !!it.enabled(group) : true; } catch(e){ ok = false; }
      var t = ""; try { t = typeof it.title === "function" ? it.title(group) : (it.title || ""); } catch(e){}
      if(it.id === "delete-group") items.push("-");
      items.push({ id: it.id, icon: it.icon, label: it.label + (it.id === "delete-group" ? "" : "\u2026"), sc: it.id === "delete-group" && !ok ? "only when empty" : "", danger: it.id === "delete-group", disabled: !ok, title: t, action: function(){ it.action(group); } });
      if(it.id === "add-subgroup"){
        items.push({ icon: group.showInRail !== false ? "\u2611" : "\u2610", label: "Show in rail", action: function(){ group.showInRail = group.showInRail === false; if(A().commit) A().commit({ tray: true }); } });
      }
    });
    items.push("-");
    items.push({ icon: "\uD83D\uDCC1", label: "New card group\u2026", disabled: !A().newGroup, action: function(){ A().newGroup(); } });
    return { caption: "Group \u00B7 " + (group.title || group.id), items: items };
  };
  REG.tray = function(){
    return { caption: "Cards", items: [
      { icon: "\uFF0B", label: "New", sub: [
        { icon: "\uD83D\uDCC1", label: "New card group\u2026", disabled: !A().newGroup, action: function(){ A().newGroup(); } },
        { icon: "\uD83D\uDDC2", label: "New setup tab\u2026", action: newSetup }
      ] },
      "-",
      { icon: "\u27F3", label: "Reload from files\u2026", title: "Reload cards, groups, setups and layouts from the data files. Nothing is deleted.", disabled: !A().reloadFromFiles, action: function(){ A().reloadFromFiles(); } }
    ] };
  };
  REG.layout = function(ctx){
    var m = M(); var lid = ctx.el.getAttribute("data-layout");
    var lay = m && m.stages && (m.stages.layouts || []).filter(function(x){ return x && x.id === lid; })[0];
    return { caption: "Layout \u00B7 " + (lay ? (lay.label || lay.id) : lid), items: [
      { icon: "\u25A6", label: "Use this layout", action: function(){ ctx.el.click(); } },
      { icon: "\u270E", label: "Edit\u2026", disabled: !lay || !A().editLayout, action: function(){ A().editLayout(lay); } },
      { icon: "\u2795", label: "Add layout\u2026", disabled: !A().addLayout, action: function(){ A().addLayout(); } },
      { icon: "\uD83D\uDCD0", label: "Stage defaults\u2026", disabled: !A().stageDefaults, action: function(){ A().stageDefaults(); } },
      "-",
      { icon: "\uD83D\uDDD1", label: "Delete layout", danger: true, disabled: !lay || !A().deleteLayout, action: function(){ A().deleteLayout(lay); } }
    ] };
  };
  REG.vault = function(ctx){
    var unlocked = ctx.el.classList.contains("unlocked");
    return { caption: "Vault", items: [
      unlocked
        ? { icon: "\uD83D\uDD12", label: "Lock vault", action: function(){ if(window.__adcVaultLock) window.__adcVaultLock(); else ctx.el.click(); } }
        : { icon: "\uD83D\uDD13", label: "Unlock vault\u2026", action: function(){ ctx.el.click(); } },
      { icon: "\uD83D\uDD11", label: "Login settings for a pane\u2026", title: "Right-click a pane header \u2192 Login settings", disabled: true, action: function(){} }
    ] };
  };

  function kindOf(t){
    if(!t || !t.closest) return null;
    if(t.closest("input,textarea,[contenteditable=true]")) return null;
    var el;
    if((el = t.closest("#tabs .tab[data-view]"))) return { kind: "tab", el: el, id: el.getAttribute("data-view") };
    if((el = t.closest("#tabs"))) return { kind: "tabrow", el: el };
    if((el = t.closest(".lp-tray .lp-tile[data-card-id]"))) return { kind: "card", el: el };
    if((el = t.closest("details.lp-group > summary"))) return { kind: "group", el: el };
    if((el = t.closest(".lp-slot .lp-pane-h[data-slot-drag]"))) return { kind: "pane", el: el };
    if((el = t.closest("#lpLayoutBtns .lp-layout-btn[data-layout]"))) return { kind: "layout", el: el };
    if((el = t.closest(".adc-vault-head"))) return { kind: "vault", el: el };
    if((el = t.closest(".lp-tray-head, .lp-tray-scroll, .lp-tray-foot"))) return { kind: "tray", el: el };
    return null;
  }
  document.addEventListener("contextmenu", function(e){
    var ctx = kindOf(e.target);
    if(!ctx || !REG[ctx.kind]) return;
    var spec = null;
    try { spec = REG[ctx.kind](ctx); } catch(err){ spec = null; }
    if(!spec) return;
    e.preventDefault(); e.stopImmediatePropagation();
    openMenu(spec.caption, spec.items, e.clientX, e.clientY);
  }, true);

  /* ---------- keys ---------- */
  document.addEventListener("keydown", function(e){
    if(e.defaultPrevented) return;
    var tag = (e.target && e.target.tagName) || "";
    var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(tag) || (e.target && e.target.isContentEditable);
    var id = solo() || activeId();
    if(e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === "s" || e.key === "S")){
      e.preventDefault(); if(id){ saveSetup(id, true); toast("Saved " + ((tabById(id) || {}).label || id)); }
      return;
    }
    if(typing) return;
    if(e.ctrlKey && e.shiftKey && !e.altKey && (e.key === "A" || e.key === "a")){ e.preventDefault(); if(A().tabList) A().tabList(); return; }
    if(e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === "w" || e.key === "W")){ e.preventDefault(); if(id && !solo()) closeSetup(id); return; }
    if(e.key === "F2" && !e.ctrlKey && !e.altKey){ e.preventDefault(); if(id && !solo()) renameSetup(id); }
  }, true);

  /* ---------- tab row decoration ---------- */
  function decorate(){
    var host = document.getElementById("tabs"); if(!host) return;
    host.querySelectorAll(".tab[data-view]").forEach(function(chip){
      var id = chip.getAttribute("data-view"), t = tabById(id);
      chip.classList.add("adc-setup-tab");
      chip.classList.remove("launch");
      chip.style.display = t && t.hidden ? "none" : "";
      if(!chip.querySelector(":scope > .adc-stab-grip")){
        var g = document.createElement("span"); g.className = "adc-stab-grip"; g.setAttribute("aria-hidden", "true");
        chip.insertBefore(g, chip.firstChild);
      }
      var l = L(); var st = l && l.stages && l.stages[id];
      var lay = chip.querySelector(":scope > .adc-stab-lay");
      if(!lay){ lay = document.createElement("span"); lay.className = "adc-stab-lay"; var lbl = chip.querySelector(":scope > .adc-tlabel"); if(lbl && lbl.nextSibling) chip.insertBefore(lay, lbl.nextSibling); else chip.appendChild(lay); }
      var lt = st ? layoutLabel(st.layout) : (t && t.stage ? layoutLabel(t.stage.layout) : "");
      if(lay.textContent !== lt) lay.textContent = lt;
      var det = chip.querySelector(".adc-tab-detach");
      if(det){ det.textContent = "\u29C9"; det.title = "Detach to a new window (or drag the tab down out of the row)"; }
      chip.querySelectorAll(".adc-tab-close").forEach(function(x){ if(!x.classList.contains("adc-stab-x")) x.remove(); });
      if(!chip.querySelector(".adc-stab-x")){
        var x = document.createElement("button");
        x.type = "button"; x.className = "adc-tab-act adc-tab-close adc-stab-x"; x.textContent = "\u00D7";
        x.title = "Close this tab (the setup stays saved; programs keep running)";
        x.addEventListener("click", function(e){ e.preventDefault(); e.stopPropagation(); closeSetup(id); });
        chip.appendChild(x);
      }
      if(!chip.dataset.adcSetupBound){
        chip.dataset.adcSetupBound = "1";
        chip.addEventListener("dblclick", function(e){ if(e.target.closest("button,input")) return; e.preventDefault(); renameSetup(id); });
        chip.addEventListener("auxclick", function(e){ if(e.button === 1){ e.preventDefault(); closeSetup(id); } });
      }
    });
    var end = document.getElementById("lpTabsEnd");
    if(!end){ var tg = document.getElementById("lpToolbarToggle"); if(tg && tg.parentNode === host) end = tg; }
    var add = host.querySelector(".adc-tab-add");
    if(!add){
      add = document.createElement("button");
      add.type = "button"; add.className = "adc-tab-add"; add.textContent = "+";
      add.title = "New setup tab";
      add.addEventListener("click", function(e){ e.preventDefault(); newSetup(); });
    }
    var hint = host.querySelector(".adc-tab-hint");
    if(!hint){ hint = document.createElement("span"); hint.className = "adc-tab-hint"; hint.textContent = "drag to reorder \u00B7 \u29C9 detach \u00B7 double-click to rename \u00B7 right-click for more"; }
    // keep '+' right after the last chip and the hint at the far right
    var chips = host.querySelectorAll(".tab[data-view]");
    var last = chips.length ? chips[chips.length - 1] : null;
    if(last && last.nextSibling !== add) host.insertBefore(add, last.nextSibling);
    else if(!last && add.parentNode !== host) host.insertBefore(add, end || null);
    if(hint.parentNode !== host || hint.nextSibling !== (end || null)) host.insertBefore(hint, end || null);
  }
  window.__adcDecorateSetups = decorate;

  function retitleTray(){
    var h = document.querySelector(".lp-tray-head h2"); if(h && h.textContent !== "Cards") h.textContent = "Cards";
    var sub = document.querySelector(".lp-tray-head .sub"); if(sub) sub.textContent = "Drag cards into panes \u00B7 right-click for more";
  }

  function boot(){
    var host = document.getElementById("tabs");
    if(!host || !L() || !M() || !window.__adcCommit){ setTimeout(boot, 250); return; }
    retitleTray();
    try { migrate(); } catch(e){ toast("Setup migration failed: " + e.message + " (nothing was deleted)"); }
    try { adoptSavedStages(); } catch(e){}
    new MutationObserver(function(){ decorate(); }).observe(host, { childList: true });
    decorate();
    setInterval(decorate, 1500); // keeps layout badges current
    tabs().forEach(function(t){ if(t && t.stage) lastSaved[t.id] = JSON.stringify(t.stage); });
    document.body.classList.add("adc-v15");
  }
  if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", function(){ setTimeout(boot, 0); });
  else setTimeout(boot, 0);
})();

/* 2026-10-11 adc-v159: drag a tab on the tab strip to reorder it, in any mode (not only in Edit).
   Before, the strip's HTML5 drag only started in Edit mode or for group tabs (bindTabStripDrag: draggable false and
   dragstart cancelled otherwise), so the setup tabs showed a grip and "drag to reorder" but would not move. This uses
   a pointer drag (like the pane headers, adc-panedrag: HTML5 drag is unreliable in WebView2): a blue bar shows where the
   tab will go; the drop saves the order (tabs.json). Dragging the tab down out of the strip (60 px) still detaches
   it, as before. Esc cancels. A plain click still switches tabs. */
(function(){
  var THRESH = 6, TEAR = 60;
  function chipOf(el){ return el && el.closest ? el.closest('#tabs .tab[data-view]') : null; }
  // The strip no longer uses HTML5 drag for tabs (the pointer drag below does reorder and detach).
  document.addEventListener('dragstart', function(e){
    if(chipOf(e.target)){ e.preventDefault(); e.stopPropagation(); }
  }, true);
  document.addEventListener('pointerdown', function(e){
    if(e.button !== 0) return;
    var chip = chipOf(e.target);
    if(!chip) return;
    if(e.target.closest('button,input,select,textarea,a,.adc-mini') || chip.querySelector('input')) return;
    var host = document.getElementById('tabs');
    if(!host) return;
    chip.setAttribute('draggable', 'false');
    var fromId = chip.getAttribute('data-view');
    var sx = e.clientX, sy = e.clientY, started = false, done = false, drop = null, ind = null, pid = e.pointerId;
    try { chip.setPointerCapture(pid); } catch(_){}
    function others(){
      return Array.prototype.filter.call(host.querySelectorAll('.tab[data-view]'), function(c){
        return c !== chip && c.offsetParent !== null && c.getClientRects().length > 0;
      });
    }
    function pick(ev){
      var r = host.getBoundingClientRect();
      if(ev.clientY > r.bottom + TEAR || ev.clientY < 0 || ev.clientX < 0 || ev.clientX > window.innerWidth || ev.clientY > window.innerHeight) return { tear: true };
      var best = null, bd = Infinity;
      others().forEach(function(c){
        var b = c.getBoundingClientRect();
        var dy = ev.clientY < b.top ? b.top - ev.clientY : (ev.clientY > b.bottom ? ev.clientY - b.bottom : 0);
        var dx = ev.clientX < b.left ? b.left - ev.clientX : (ev.clientX > b.right ? ev.clientX - b.right : 0);
        var d = dy * 4 + dx;
        if(d < bd){ bd = d; best = { c: c, b: b }; }
      });
      if(!best) return null;
      return { id: best.c.getAttribute('data-view'), after: ev.clientX > best.b.left + best.b.width / 2, b: best.b };
    }
    function show(p){
      if(!ind){ ind = document.createElement('div'); ind.className = 'adc-tab-drop-ind'; document.body.appendChild(ind); }
      if(!p || p.tear){ ind.style.display = 'none'; }
      else {
        ind.style.display = 'block';
        ind.style.left = ((p.after ? p.b.right : p.b.left) - 1.5) + 'px';
        ind.style.top = (p.b.top - 3) + 'px';
        ind.style.height = (p.b.height + 6) + 'px';
      }
      chip.classList.toggle('adc-tab-tear', !!(p && p.tear));
    }
    function move(ev){
      if(done) return;
      if(ev.type === 'mousemove' && ev.buttons === 0){ finish(ev, false); return; }
      if(!started){
        if(Math.abs(ev.clientX - sx) + Math.abs(ev.clientY - sy) < THRESH) return;
        started = true;
        chip.classList.add('tab-dragging');
        document.body.classList.add('adc-tab-reordering');
        document.body.classList.add('adc-dragging'); // native panes step aside while a tab is dragged (as before)
      }
      drop = pick(ev);
      show(drop);
    }
    function finish(ev, cancel){
      if(done) return; done = true;
      window.removeEventListener('pointermove', move, true);
      window.removeEventListener('mousemove', move, true);
      window.removeEventListener('pointerup', up, true);
      window.removeEventListener('mouseup', up, true);
      window.removeEventListener('pointercancel', cxl, true);
      document.removeEventListener('keydown', esc, true);
      try { chip.releasePointerCapture(pid); } catch(_){}
      if(ind) ind.remove();
      if(!started) return;
      chip.classList.remove('tab-dragging');
      chip.classList.remove('adc-tab-tear');
      document.body.classList.remove('adc-tab-reordering');
      document.body.classList.remove('adc-dragging');
      // the click that follows a drag must not switch tabs
      window.__adcSuppressTabClick = true;
      setTimeout(function(){ window.__adcSuppressTabClick = false; }, 400);
      if(cancel || !drop) return;
      if(drop.tear){ if(window.__adcDetachTab) window.__adcDetachTab(fromId); return; }
      if(window.__adcMoveTab) window.__adcMoveTab(fromId, drop.id, drop.after);
    }
    function up(ev){ finish(ev, false); }
    function cxl(ev){ finish(ev, true); }
    function esc(ev){ if(ev.key === 'Escape'){ ev.preventDefault(); ev.stopPropagation(); finish(ev, true); } }
    window.addEventListener('pointermove', move, true);
    window.addEventListener('mousemove', move, true);
    window.addEventListener('pointerup', up, true);
    window.addEventListener('mouseup', up, true);
    window.addEventListener('pointercancel', cxl, true);
    document.addEventListener('keydown', esc, true);
  }, true);
})();

/* 2026-10-12 adc-topcol (v1.5.12 / DB 1.5.10): collapse the top section per group (approved mockup v2).
   - "Collapse top" button next to "Hide toolbars" (both kept). Collapsed = title bar, group tabs and layout toolbar
     fold into a 6 px strip; the panes get the height (111 -> 6 px).
   - Hover the strip ~250 ms = peek: the top comes back and PUSHES the panes down (native panes would hide anything
     drawn over them); it folds again ~750 ms after the mouse leaves the top. Click the strip = expand for good.
   - Ctrl+Alt+T toggles. The WV2 host registers it while this cockpit (or one of its panes) is the foreground window,
     so it also works while an embedded program or web pane has focus (host message "adc-topcol-toggle").
   - Saved per group as "topCollapsed": true on the tab in tabs.json (omitted when expanded). Collapsed groups show a
     small marker on their tab. Panes follow automatically: the slot ResizeObserver reports the new pane bounds. */
(function(){
  "use strict";
  if(window.__adcTopCol) return;
  var PEEK_DELAY = 250, LEAVE_DELAY = 750;
  var body = document.body, strip = null, btn = null, badge = null, peekTimer = 0, leaveTimer = 0, lastActive = "";
  function model(){ return window.__ADC && window.__ADC.model; }
  function activeId(){ var c = document.querySelector("#tabs .tab.active[data-view]"); return c ? c.getAttribute("data-view") : ""; }
  function tabById(id){ var m = model(); var l = (m && m.tabs) || []; for(var i = 0; i < l.length; i++){ if(l[i] && l[i].id === id) return l[i]; } return null; }
  function isCollapsed(id){ var t = tabById(id); return !!(t && t.topCollapsed === true); }
  function relayout(){
    setTimeout(function(){
      try { if(typeof window.__adcReportSlots === "function") window.__adcReportSlots(); } catch(_){}
      try { window.dispatchEvent(new Event("resize")); } catch(_){}
    }, 30);
  }
  function ensureUi(){
    var app = document.querySelector(".app") || body;
    if(!strip || !strip.isConnected){
      strip = document.createElement("div");
      strip.id = "adcTopStrip";
      strip.setAttribute("role", "button");
      strip.tabIndex = 0;
      strip.title = "Top bar collapsed for this group - hover to peek, click to expand (Ctrl+Alt+T)";
      strip.setAttribute("aria-label", strip.title);
      app.insertBefore(strip, app.firstChild);
      strip.addEventListener("mouseenter", function(){ clearTimeout(leaveTimer); clearTimeout(peekTimer); peekTimer = setTimeout(function(){ setPeek(true); }, PEEK_DELAY); });
      strip.addEventListener("mouseleave", function(){ clearTimeout(peekTimer); });
      strip.addEventListener("click", function(e){ e.preventDefault(); clearTimeout(peekTimer); setCollapsed(activeId(), false); });
      strip.addEventListener("keydown", function(e){ if(e.key === "Enter" || e.key === " "){ e.preventDefault(); setCollapsed(activeId(), false); } });
    }
    var toggle = document.getElementById("lpToolbarToggle");
    if(!btn){
      btn = document.createElement("button");
      btn.type = "button";
      btn.id = "adcTopColBtn";
      btn.className = "lp-toolbar-toggle adc-topcol-btn";
      btn.innerHTML = '<span class="tc-chev" aria-hidden="true">\u25B2</span> <span class="tc-lbl">Collapse top</span>';
      btn.addEventListener("click", function(e){ e.stopPropagation(); var id = activeId(); setCollapsed(id, !isCollapsed(id)); });
    }
    if(toggle && toggle.parentNode && btn.nextSibling !== toggle) toggle.parentNode.insertBefore(btn, toggle);
    else if(!toggle && !btn.isConnected){ var tools = document.querySelector("header .adc-head-tools") || document.querySelector("header"); if(tools) tools.appendChild(btn); }
    var h = document.querySelector("header");
    if(h && (!badge || !badge.isConnected)){
      badge = document.createElement("span");
      badge.className = "adc-topcol-peekbadge";
      badge.textContent = "PEEK - click the strip or Ctrl+Alt+T to keep it open";
      h.appendChild(badge);
    }
  }
  function paintMarks(){
    document.querySelectorAll("#tabs .tab[data-view]").forEach(function(chip){
      var on = isCollapsed(chip.getAttribute("data-view"));
      var mk = chip.querySelector(".adc-topcol-mark");
      if(on && !mk){
        mk = document.createElement("span");
        mk.className = "adc-topcol-mark";
        mk.textContent = "\u25B2";
        mk.title = "Top bar collapsed in this group";
        var lbl = chip.querySelector(".adc-tlabel");
        if(lbl && lbl.nextSibling) chip.insertBefore(mk, lbl.nextSibling); else chip.appendChild(mk);
      } else if(!on && mk) mk.remove();
    });
  }
  function apply(){
    ensureUi();
    var id = activeId();
    var col = isCollapsed(id);
    var was = body.classList.contains("adc-topcol");
    if(id !== lastActive){ lastActive = id; setPeek(false, true); }
    body.classList.toggle("adc-topcol", col);
    if(!col) setPeek(false, true);
    if(btn){
      btn.setAttribute("aria-pressed", col ? "true" : "false");
      btn.title = col ? "Expand the top for this group (Ctrl+Alt+T)" : "Collapse the top for this group (Ctrl+Alt+T)";
      btn.querySelector(".tc-chev").textContent = col ? "\u25BC" : "\u25B2";
      btn.querySelector(".tc-lbl").textContent = col ? "Expand top" : "Collapse top";
    }
    paintMarks();
    if(was !== col) relayout();
  }
  function setPeek(on, quiet){
    clearTimeout(leaveTimer);
    on = !!on && body.classList.contains("adc-topcol");
    if(body.classList.contains("adc-topcol-peek") === on) return;
    body.classList.toggle("adc-topcol-peek", on);
    if(!quiet) relayout();
  }
  function setCollapsed(id, c){
    var t = tabById(id);
    if(!t) return false;
    if(c) t.topCollapsed = true; else delete t.topCollapsed;
    body.classList.remove("adc-topcol-peek");
    apply();
    relayout();
    try { if(typeof window.__adcSaveOverlay === "function") window.__adcSaveOverlay(); } catch(_){}
    return true;
  }
  function toggle(){ var id = activeId(); return setCollapsed(id, !isCollapsed(id)); }
  /* peek ends when the pointer leaves the top section: either it moves below the last top row inside the page,
     or it leaves the page for a native pane (document mouseleave). */
  function topBottom(){
    var b = 0;
    ["header", "#tabs", "#lpToolbar"].forEach(function(q){ var n = document.querySelector(q); if(n && n.offsetParent !== null){ var r = n.getBoundingClientRect(); if(r.height > 0) b = Math.max(b, r.bottom); } });
    return b;
  }
  function maybeLeave(y){
    if(!body.classList.contains("adc-topcol-peek")) return;
    if(y !== null && y <= topBottom() + 2){ clearTimeout(leaveTimer); return; }
    if(document.querySelector(".lp-dropdown.open, .dropdown.open, .adc-menu.open")) return; /* a menu from the top is open */
    clearTimeout(leaveTimer);
    leaveTimer = setTimeout(function(){ setPeek(false); }, LEAVE_DELAY);
  }
  document.addEventListener("mousemove", function(e){ maybeLeave(e.clientY); }, true);
  document.documentElement.addEventListener("mouseleave", function(){ maybeLeave(null); });
  document.addEventListener("keydown", function(e){
    if(e.ctrlKey && e.altKey && !e.shiftKey && (e.key === "t" || e.key === "T" || e.code === "KeyT")){ e.preventDefault(); toggle(); }
  }, true);
  try {
    var wv = window.chrome && window.chrome.webview;
    if(wv) wv.addEventListener("message", function(e){ var d = e && e.data; if(d && d.type === "adc-topcol-toggle") toggle(); });
  } catch(_){}
  /* the tab strip re-renders (live data poll, switches, renames): re-apply the active group's state */
  var queued = false;
  function queueApply(){ if(queued) return; queued = true; setTimeout(function(){ queued = false; apply(); }, 0); }
  function start(){
    apply();
    var tabs = document.getElementById("tabs");
    if(tabs && window.MutationObserver) new MutationObserver(queueApply).observe(tabs, { childList: true, subtree: true, attributes: true, attributeFilter: ["class"] });
    setInterval(apply, 1500); /* model swaps from the live poll without a DOM change */
  }
  window.__adcTopCol = { toggle: toggle, set: setCollapsed, peek: setPeek, apply: apply, isCollapsed: isCollapsed };
  if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", function(){ setTimeout(start, 0); }); else setTimeout(start, 0);
})();
