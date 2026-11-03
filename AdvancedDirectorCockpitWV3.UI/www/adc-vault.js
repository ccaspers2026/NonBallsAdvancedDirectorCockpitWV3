/* ADC per-card password vault (2026-10-09).
   Login button on every pane header, PIN unlock once per cockpit session, Lock in the title row.
   The vault itself runs inside the WV3 host (in-process, /adc-vault/*). Encryption and the PIN
   check happen there. This page only holds a random session token in memory (never localStorage).
   Secrets are never logged, never put in a URL, and never stored by this page. */
(function(){
  'use strict';
  if(window.__adcVaultLoaded) return;
  window.__adcVaultLoaded = 1;

  var WV2 = /^adc-wv2\.local$/i.test(location.hostname);
  /* Same origin in both cockpits: Caddy forwards /adc-vault/* (loopback only) in the browser cockpit;
     the WV3 host answers https://adc-wv2.local/adc-vault/* in-process. No CORS needed. */
  var BASE = '';
  var nativeFetch = window.fetch.bind(window);
  var host = WV2 && window.chrome && window.chrome.webview;
  var token = '';
  var ids = {};
  var initialized = false;
  var minPin = 6;
  var autoPromptDeclined = false;
  var pinPromise = null;
  var pingTimer = 0;
  var refusedOnce = {};

  function adcVaultFill(win, doc, d){
    if(!d || !doc || !doc.documentElement) return;
    /* One watcher per document. A new call (manual Fill login, or a re-run) replaces the old one. */
    try { if(win.__adcVaultFillCtl && win.__adcVaultFillCtl.stop) win.__adcVaultFillCtl.stop(); } catch(e){}
    var filled = new win.WeakSet(), advanced = new win.WeakSet(), submitted = new win.WeakSet(), mine = new win.WeakSet();
    var start = Date.now(), LIFE = 15 * 60 * 1000, stopped = false, timer = 0, obs = null, count = 0;
    function blob(el){ return ((el.getAttribute('autocomplete') || '') + ' ' + (el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('placeholder') || '') + ' ' + (el.getAttribute('data-testid') || '')).toLowerCase(); }
    function typ(el){ return String(el.type || 'text').toLowerCase(); }
    function vis(el){
      if(!el || el.disabled || el.readOnly || typ(el) === 'hidden') return false;
      try {
        var r = el.getBoundingClientRect();
        if(!(r.width > 0 && r.height > 0)) return false;
        var cs = win.getComputedStyle(el);
        return cs.visibility !== 'hidden' && cs.display !== 'none' && Number(cs.opacity || 1) > 0.05;
      } catch(e){ return false; }
    }
    function isOtp(el){ return /one-time-code|one-time|\botp\b|totp|mfa|2fa|verification|security.?code|passcode|\bpin\b|captcha|search/.test(blob(el)); }
    function isPass(el){ return typ(el) === 'password' && !isOtp(el); }
    function isIdent(el){
      var t = typ(el);
      if(t !== 'text' && t !== 'email' && t !== 'tel' && t !== '' && t !== 'number') return false;
      if(isOtp(el)) return false;
      if(t === 'email' || t === 'tel') return true;
      var b = blob(el);
      return /\b(username|email|tel)\b/.test(el.getAttribute('autocomplete') || '') ||
        /user|e-?mail|login|logon|userid|account|phone|mobile|cell|identifier|member/.test(b);
    }
    function setVal(el, v){
      try {
        var proto = win.HTMLInputElement && el instanceof win.HTMLInputElement ? win.HTMLInputElement.prototype : null;
        var desc = proto && Object.getOwnPropertyDescriptor(proto, 'value');
        if(el.focus) el.focus();
        /* Native setter so React/MUI/Vue see a real change, then the events a typing user would fire. */
        if(desc && desc.set) desc.set.call(el, v); else el.value = v;
        var ie;
        try { ie = new win.InputEvent('input', { bubbles: true, cancelable: false, inputType: 'insertText', data: String(v) }); }
        catch(e2){ ie = new win.Event('input', { bubbles: true }); }
        el.dispatchEvent(ie);
        el.dispatchEvent(new win.Event('change', { bubbles: true }));
        try { el.dispatchEvent(new win.KeyboardEvent('keyup', { bubbles: true, key: 'Unidentified' })); } catch(e3){}
        count++;
      } catch(e){}
    }
    /* Phone fields often cap length or reject punctuation: fall back to digits when the stored text does not fit. */
    function identValue(el, v){
      var max = parseInt(el.getAttribute('maxlength') || '0', 10);
      var phoneish = typ(el) === 'tel' || /phone|mobile|cell|\btel\b/.test(blob(el));
      var digits = String(v).replace(/[^0-9]/g, '');
      if(phoneish && digits.length >= 7 && /^[\s0-9()+.\-]+$/.test(String(v))){
        if(digits.length === 11 && digits.charAt(0) === '1' && (max === 10 || max === 14)) digits = digits.slice(1);
        if(max > 0 && String(v).length > max) return digits.slice(0, max);
        if(typ(el) === 'number') return digits;
      }
      return v;
    }
    /* Small notice inside the login page itself (the cockpit page cannot see into the pane). Never shows values. */
    function note(msg){
      try {
        var n = doc.createElement('div');
        n.textContent = msg;
        n.setAttribute('style', 'position:fixed;z-index:2147483647;left:50%;bottom:24px;transform:translateX(-50%);background:#1f2937;color:#fff;font:14px/1.4 system-ui,sans-serif;padding:8px 14px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.35);pointer-events:none');
        (doc.body || doc.documentElement).appendChild(n);
        setTimeout(function(){ try { n.remove(); } catch(e){} }, 3500);
      } catch(e){}
    }
    function enabled(el){ return !!el && !el.disabled && String(el.getAttribute('aria-disabled') || '').toLowerCase() !== 'true' && vis(el); }
    function label(el){ return String((el.innerText || el.textContent || el.value || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || '')).replace(/\s+/g, ' ').trim(); }
    var GO = /^(next|continue|send( me a)? code|send|get code|text me( a code)?|verify|sign ?in|log ?in|login|submit|proceed|go)\b/i;
    var NOGO = /google|apple|facebook|microsoft|sso|create|sign ?up|register|forgot|reset|help|back|cancel|privacy|terms|password instead|different/i;
    /* Returns {el, how} for the Next-style control, preferring the form's own submit button. */
    function advanceTarget(field){
      var f = field && field.form;
      if(f){
        var subs = Array.prototype.slice.call(f.querySelectorAll('button[type=submit], input[type=submit], button:not([type])')).filter(function(b){ return vis(b) && !NOGO.test(label(b)); });
        if(subs.length) return { el: subs[0], how: 'button' };
      }
      var all = Array.prototype.slice.call(doc.querySelectorAll('button, input[type=submit], input[type=button], [role=button], a[href]')).filter(vis);
      for(var i = 0; i < all.length; i++){
        var t = label(all[i]);
        if(t && t.length < 40 && GO.test(t) && !NOGO.test(t)) return { el: all[i], how: 'text' };
      }
      if(f && f.requestSubmit) return { el: null, how: 'form' };
      return null;
    }
    /* Waits up to 3 s for the button to enable (React often enables it only after the input event), then presses once. */
    function press(field, set, what){
      if(set.has(field)) return;
      set.add(field);
      var t0 = Date.now();
      (function attempt(){
        try {
          if(stopped) return;
          var tg = advanceTarget(field);
          if(tg && tg.el && enabled(tg.el)){
            tg.el.click();
            note(what + ': clicked ' + (label(tg.el).slice(0, 24) || 'Next'));
            return;
          }
          if(Date.now() - t0 < 3000){ setTimeout(attempt, 150); return; }
          var f = field.form;
          if(tg && tg.how === 'form' && f && f.requestSubmit){ f.requestSubmit(); note(what + ': submitted the form'); return; }
          note(what + (tg && tg.el ? ': the ' + (label(tg.el).slice(0, 24) || 'Next') + ' button stayed disabled' : ': no button found') + ' - press it yourself');
        } catch(e){}
      })();
    }
    function pass1(){
      if(stopped) return;
      if(Date.now() - start > LIFE){ stop(); return; }
      var list = Array.prototype.slice.call(doc.querySelectorAll('input')).filter(vis);
      var passes = list.filter(isPass);
      var idents = list.filter(isIdent);
      if(passes.length){
        var pass = passes[0];
        /* Same-screen username: the identifier just before the password, only if empty. */
        if(d.u){
          var best = null, idx = list.indexOf(pass);
          for(var i = 0; i < idx; i++){ if(isIdent(list[i]) || (typ(list[i]) === 'text' && !isOtp(list[i]))) best = list[i]; }
          if(best && !filled.has(best) && !best.value){ setVal(best, identValue(best, d.u)); filled.add(best); }
        }
        /* r7.2: an automatic (re)start never overwrites a password the user already typed; only a manual Fill (d.force) does. */
        if(d.p && !filled.has(pass) && isPass(pass)){ if(!pass.value || d.force){ setVal(pass, d.p); mine.add(pass); } filled.add(pass); }
        if(d.autoSubmit && mine.has(pass) && pass.value === d.p) press(pass, submitted, 'Auto-submit');
      } else if(d.u && idents.length){
        /* Identifier-only screen (phone, email, or user id). */
        var id = idents[0];
        if(!filled.has(id)){
          if(!id.value || id.value === d.u){ setVal(id, identValue(id, d.u)); }
          filled.add(id);
          if(d.autoAdvance && id.value) press(id, advanced, 'Auto-advance');
        }
      }
      if(d.x && d.xl){
        var key = String(d.xl).toLowerCase();
        var xf = list.filter(function(el){ return !isPass(el) && typ(el) !== 'password' && blob(el).indexOf(key) >= 0; })[0] || null;
        if(xf && !filled.has(xf)){ setVal(xf, d.x); filled.add(xf); }
      }
    }
    function queue(){ if(timer || stopped) return; timer = setTimeout(function(){ timer = 0; pass1(); }, 150); }
    function stop(){
      stopped = true;
      try { if(obs) obs.disconnect(); } catch(e){}
      clearTimeout(timer);
      d = null;
    }
    win.__adcVaultFillCtl = { stop: stop };
    pass1();
    try {
      obs = new win.MutationObserver(queue);
      obs.observe(doc.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden', 'type', 'disabled'] });
    } catch(e){}
    try { win.addEventListener('pagehide', stop); } catch(e){}
    /* SPA screens that swap without DOM mutations we can see (rare): re-check now and then. */
    (function tick(){ if(stopped) return; pass1(); setTimeout(tick, 1500); })();
    return count;
  }

  function api(action, body){
    var headers = { 'Content-Type': 'application/json' };
    if(token) headers['X-ADC-Vault-Token'] = token;
    return nativeFetch(BASE + '/adc-vault/' + action, {
      method: 'POST', headers: headers, body: JSON.stringify(body || {}),
      cache: 'no-store', credentials: 'omit', mode: 'same-origin', redirect: 'error'
    }).then(function(r){
      return r.json().catch(function(){ return {}; }).then(function(j){ j = j || {}; j.__status = r.status; return j; });
    }, function(){ return { __status: 0, error: 'The vault did not answer (in-process vault in the cockpit host).' }; });
  }

  function refreshStatus(){
    return api('status').then(function(s){
      if(s.__status !== 200) return s;
      initialized = !!s.initialized;
      minPin = s.minPin || minPin;
      ids = {};
      (s.ids || []).forEach(function(id){ ids[id] = 1; });
      if(token && !s.unlocked) setToken('');
      paintButtons();
      return s;
    });
  }

  function setToken(t){
    token = t || '';
    clearInterval(pingTimer);
    if(token){
      pingTimer = setInterval(function(){
        api('ping').then(function(r){ if(r.__status === 401){ setToken(''); } });
      }, 30000);
    }
    paintHeader();
    paintButtons();
  }

  /* ---------- styles ---------- */
  var css = '' +
    '.adc-vault-modal .lp-modal{max-width:480px;width:min(480px,92vw);height:auto;min-height:0;resize:none}' +
    '.adc-vault-modal label{display:block;margin:8px 0 2px;font-size:12px;opacity:.85}' +
    '.adc-vault-modal input[type=text],.adc-vault-modal input[type=password],.adc-vault-modal textarea{width:100%;box-sizing:border-box;padding:6px 8px;font:inherit}' +
    '.adc-vault-modal textarea{min-height:52px;resize:vertical}' +
    '.adc-vault-modal .adc-vault-row{display:flex;gap:8px}.adc-vault-modal .adc-vault-row>div{flex:1}' +
    '.adc-vault-modal .adc-vault-err{color:#ff8a80;min-height:1.2em;margin-top:6px;font-size:12px}' +
    '.adc-vault-modal .adc-vault-check{display:flex;align-items:center;gap:6px;margin-top:10px}' +
    '.adc-vault-modal .adc-vault-check input{width:auto}' +
    '.adc-vault-modal .adc-vault-tools{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:6px}' +
    '.lp-pane-h button[data-login-slot].adc-vault-has::before{content:"\\1F511 ";}' +
    '.adc-vault-head.unlocked{border-color:#4caf50}';
  var st = document.createElement('style');
  st.textContent = css;
  (document.head || document.documentElement).appendChild(st);

  /* ---------- dialog (its own backdrop; the WV2 popup watcher hides panes while it shows) ---------- */
  var modal = null;
  function closeDialog(){
    if(!modal) return;
    modal.classList.remove('show');
    modal.setAttribute('aria-hidden', 'true');
    var body = modal.querySelector('.adc-vault-body');
    if(body) body.innerHTML = '';
  }
  function dialog(title, hint, build, actions){
    if(!modal){
      modal = document.createElement('div');
      modal.className = 'lp-modal-backdrop adc-vault-modal';
      modal.setAttribute('aria-hidden', 'true');
      modal.innerHTML = '<div class="lp-modal" role="dialog" aria-modal="true"><h3 class="adc-vault-title"></h3><p class="hint adc-vault-hint"></p><div class="adc-vault-body"></div><div class="adc-vault-err" role="alert"></div><div class="lp-modal-actions adc-vault-actions"></div></div>';
      document.body.appendChild(modal);
      modal.addEventListener('keydown', function(e){
        if(e.key === 'Escape'){ e.stopPropagation(); var c = modal.__cancel; closeDialog(); if(c) c(); }
      });
    }
    modal.querySelector('.adc-vault-title').textContent = title;
    var h = modal.querySelector('.adc-vault-hint');
    h.textContent = hint || ''; h.hidden = !hint;
    var body = modal.querySelector('.adc-vault-body');
    body.innerHTML = '';
    build(body);
    modal.querySelector('.adc-vault-err').textContent = '';
    var bar = modal.querySelector('.adc-vault-actions');
    bar.innerHTML = '';
    modal.__cancel = null;
    actions.forEach(function(a){
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = a.label;
      if(a.primary) b.className = 'primary';
      if(a.cancel) modal.__cancel = a.onClick;
      b.addEventListener('click', function(){
        if(a.cancel){ closeDialog(); if(a.onClick) a.onClick(); return; }
        Promise.resolve(a.onClick && a.onClick()).then(function(keep){ if(keep !== true) closeDialog(); });
      });
      bar.appendChild(b);
    });
    body.addEventListener('keydown', function(e){
      if(e.key === 'Enter' && e.target && e.target.tagName === 'INPUT'){
        e.preventDefault();
        var p = bar.querySelector('button.primary'); if(p) p.click();
      }
    });
    modal.classList.add('show');
    modal.setAttribute('aria-hidden', 'false');
    var f = body.querySelector('input');
    if(f) setTimeout(function(){ try { f.focus(); } catch(_){} }, 30);
  }
  function err(msg){ if(modal) modal.querySelector('.adc-vault-err').textContent = msg || ''; }
  function field(parent, label, type, attrs){
    var l = document.createElement('label'); l.textContent = label; parent.appendChild(l);
    var i = document.createElement(type === 'textarea' ? 'textarea' : 'input');
    if(type !== 'textarea') i.type = type;
    i.autocomplete = 'off'; i.spellcheck = false;
    Object.keys(attrs || {}).forEach(function(k){ i.setAttribute(k, attrs[k]); });
    l.appendChild(i);
    return i;
  }

  /* ---------- PIN ---------- */
  function ensureUnlocked(reason){
    if(token) return Promise.resolve(true);
    if(pinPromise) return pinPromise;
    pinPromise = refreshStatus().then(function(s){
      if(s.__status !== 200){ toast(s.error || 'Vault is not reachable'); return false; }
      if(token) return true;
      return new Promise(function(resolve){
        var first = !initialized, pin1, pin2;
        dialog(first ? 'Set your vault PIN' : 'Unlock the vault',
          first ? ('First use. Pick a PIN of at least ' + minPin + ' characters and enter it twice. It is asked once per cockpit session. There is no recovery: if you forget it, the vault must be reset.')
                : (reason || 'Enter your PIN. It stays unlocked until you press Lock or close the cockpit.'),
          function(b){
            pin1 = field(b, first ? 'New PIN' : 'PIN', 'password', { inputmode: 'numeric', maxlength: '64' });
            if(first) pin2 = field(b, 'Enter the PIN again', 'password', { inputmode: 'numeric', maxlength: '64' });
          },
          [
            { label: 'Cancel', cancel: true, onClick: function(){ resolve(false); } },
            { label: first ? 'Set PIN' : 'Unlock', primary: true, onClick: function(){
              var a = pin1.value, c = pin2 ? pin2.value : a;
              if(a.length < minPin){ err('The PIN needs at least ' + minPin + ' characters.'); return true; }
              if(a !== c){ err('The two PINs do not match.'); pin2.value = ''; return true; }
              err('Checking...');
              return api(first ? 'setup' : 'unlock', { pin: a }).then(function(r){
                pin1.value = ''; if(pin2) pin2.value = ''; a = c = '';
                if(r.__status === 200 && r.token){ initialized = true; setToken(r.token); refreshStatus(); toast('Vault unlocked for this session'); resolve(true); return false; }
                err(r.error || ('Vault error ' + r.__status));
                return true;
              });
            } }
          ]);
      });
    }).then(function(ok){ pinPromise = null; return ok; }, function(){ pinPromise = null; return false; });
    return pinPromise;
  }

  function lockVault(){
    if(!token){ paintHeader(); return; }
    api('lock').then(function(){ setToken(''); toast('Vault locked'); });
  }

  /* ---------- copy to clipboard ---------- */
  var FIELD_NAMES = { u: 'username', p: 'password', x: 'extra field' };
  var clearTimer = 0;
  function writeClip(text){
    if(navigator.clipboard && navigator.clipboard.writeText) return navigator.clipboard.writeText(text);
    return new Promise(function(resolve, reject){
      var ta = document.createElement('textarea');
      ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.left = '-9999px';
      document.body.appendChild(ta); ta.select();
      var ok = false; try { ok = document.execCommand('copy'); } catch(_){}
      ta.value = ''; document.body.removeChild(ta);
      ok ? resolve() : reject(new Error('copy'));
    });
  }
  /* After 30 s, clear the clipboard only if it still holds the copied value. */
  function scheduleClear(value){
    clearTimeout(clearTimer);
    clearTimer = setTimeout(function(){
      var v = value; value = null;
      if(!navigator.clipboard || !navigator.clipboard.readText){ v = null; return; }
      navigator.clipboard.readText().then(function(now){
        if(now === v) return writeClip('').catch(function(){});
      }).catch(function(){}).then(function(){ v = null; });
    }, 30000);
  }
  function copyFor(cardId, name, field){
    if(!cardId){ toast('This pane has no saved card'); return; }
    ensureUnlocked('Enter your PIN to copy the ' + FIELD_NAMES[field] + ' for ' + name + '.').then(function(ok){
      if(!ok) return;
      api('copy', { cardId: cardId, field: field }).then(function(r){
        if(r.__status === 401){ setToken(''); toast('Vault locked - try again'); return; }
        if(r.__status !== 200 || typeof r.value !== 'string'){ toast(r.error || 'Nothing to copy'); return; }
        var v = r.value; r.value = ''; r = null;
        writeClip(v).then(function(){
          toast('Copied ' + FIELD_NAMES[field] + ' for ' + name + ' (clipboard clears in 30 s)');
          scheduleClear(v); v = null;
        }, function(){ v = null; toast('Copy was blocked - click into the cockpit window and try again'); });
      });
    });
  }
  function copySlot(slot, field){
    var info = paneInfo(slot);
    if(!info || !info.cardId){ toast('This pane has no saved card'); return; }
    copyFor(info.cardId, info.name, field);
  }

  /* ---------- Login dialog ---------- */
  function paneInfo(slot){
    try { return typeof window.__adcPaneInfo === 'function' ? window.__adcPaneInfo(slot) : null; } catch(_){ return null; }
  }
  function openLogin(slot){
    var info = paneInfo(slot);
    if(!info || !info.cardId){ toast('This pane has no saved card, so it cannot hold a login'); return; }
    ensureUnlocked('Enter your PIN to edit the login for ' + info.name + '.').then(function(ok){
      if(!ok) return;
      api('get', { cardId: info.cardId }).then(function(cur){
        if(cur.__status === 401){ setToken(''); return openLogin(slot); }
        if(cur.__status !== 200){ toast(cur.error || 'Vault error'); return; }
        var host = '';
        try { host = new URL(info.url).hostname; } catch(_){}
        var u, p, xl, x, hosts, auto, adv;
        dialog('Login for ' + info.name,
          (cur.exists ? ('A login is saved for this card' + (cur.username ? ' (username ' + cur.username + ')' : '') + (cur.hasPassword ? ', password saved (hidden; leave the box blank to keep it)' : ', no password') + (cur.sharedFromOtherCard ? ', found by site ' + (cur.savedFor || host) : '') + '. ')
                      : 'No login saved for this card yet. ') +
          'Saved encrypted in the vault, not in cards.json. Filled only on ' + (host || 'this card\'s site') + ' or the extra sites listed below.',
          function(b){
            u = field(b, 'Username (email, phone number, or user id)', 'text');
            u.value = cur.username || '';
            p = field(b, 'Password', 'password', { placeholder: cur.hasPassword ? 'saved - leave blank to keep' : '' });
            var tools = document.createElement('div'); tools.className = 'adc-vault-tools';
            function tb(label, title, fn){ var x_ = document.createElement('button'); x_.type = 'button'; x_.textContent = label; x_.title = title; x_.addEventListener('click', function(ev){ ev.preventDefault(); fn(x_); }); tools.appendChild(x_); return x_; }
            if(cur.exists){
              tb('Copy username', 'Copy the saved username', function(){ copyFor(info.cardId, info.name, 'u'); });
              if(cur.hasPassword) tb('Copy password', 'Copy the saved password', function(){ copyFor(info.cardId, info.name, 'p'); });
              if(cur.hasExtra) tb('Copy extra', 'Copy the saved extra field', function(){ copyFor(info.cardId, info.name, 'x'); });
              if(cur.hasPassword) tb('\uD83D\uDC41 Show password', 'Show or hide the saved password in the box', function(btn){
                if(p.type === 'text'){ p.type = 'password'; if(p.__revealed){ p.value = ''; p.__revealed = false; } btn.textContent = '\uD83D\uDC41 Show password'; return; }
                api('copy', { cardId: info.cardId, field: 'p' }).then(function(r){
                  if(r.__status !== 200){ err(r.error || 'Could not show it'); return; }
                  if(!p.value){ p.value = r.value; p.__revealed = true; }
                  r.value = ''; p.type = 'text'; btn.textContent = '\uD83D\uDC41 Hide password';
                });
              });
            }
            b.insertBefore(tools, b.firstChild);
            var row = document.createElement('div'); row.className = 'adc-vault-row'; b.appendChild(row);
            var c1 = document.createElement('div'), c2 = document.createElement('div'); row.appendChild(c1); row.appendChild(c2);
            xl = field(c1, 'Extra field name (optional)', 'text', { placeholder: 'e.g. account, zip' });
            xl.value = cur.extraLabel || '';
            x = field(c2, 'Extra field value', 'password', { placeholder: cur.hasExtra ? 'saved - leave blank to keep' : '' });
            hosts = field(b, 'Also fill on these sites (one per line, for sign-in redirects; *.example.com allows subdomains)', 'textarea');
            hosts.value = (cur.allowedHosts || []).join('\n');
            var ck = document.createElement('label'); ck.className = 'adc-vault-check';
            auto = document.createElement('input'); auto.type = 'checkbox'; auto.checked = !!cur.autoSubmit;
            ck.appendChild(auto); ck.appendChild(document.createTextNode('Auto-submit after filling the password (off = fill only)'));
            b.appendChild(ck);
            var ck2 = document.createElement('label'); ck2.className = 'adc-vault-check';
            adv = document.createElement('input'); adv.type = 'checkbox'; adv.checked = !!cur.autoAdvance;
            ck2.appendChild(adv); ck2.appendChild(document.createTextNode('Auto-advance: press Next/Continue after filling a username-only screen'));
            b.appendChild(ck2);
          },
          [
            { label: 'Cancel', cancel: true },
            { label: 'Delete login', onClick: function(){
              if(!cur.exists){ err('Nothing saved for this card yet.'); return true; }
              if(!window.confirm('Delete the saved login for ' + info.name + '?')) return true;
              return api('delete', { cardId: info.cardId }).then(function(r){
                if(r.__status !== 200){ err(r.error || 'Not deleted'); return true; }
                delete ids[info.cardId]; paintButtons(); toast('Login deleted for ' + info.name); return false;
              });
            } },
            { label: 'Lock vault', onClick: function(){ lockVault(); return false; } },
            { label: 'Save', primary: true, onClick: function(){
              var body = { cardId: info.cardId, username: u.value, password: p.value, extraLabel: xl.value, extra: x.value,
                           clearExtra: !xl.value, allowedHosts: hosts.value, autoSubmit: auto.checked, autoAdvance: adv.checked };
              if(!body.username && !body.password && !cur.hasPassword){ err('Enter a username or password.'); return true; }
              return api('save', body).then(function(r){
                p.value = ''; x.value = ''; body = null;
                if(r.__status !== 200){ err(r.error || ('Not saved (' + r.__status + ')')); return true; }
                ids[info.cardId] = 1; frameRefused = {}; paintButtons(); toast('Login saved for ' + info.name);
                return false;
              });
            } }
          ]);
      });
    });
  }

  /* ---------- fill ---------- */
  function hostOf(url){ try { return new URL(url).host; } catch(_){ return String(url || ''); } }
  function fetchFill(slot, url, manual){
    var info = paneInfo(slot);
    if(!info || !info.cardId || !ids[info.cardId]){
      if(manual) toast(info && info.cardId ? 'No saved login for ' + (hostOf(url) || info.name) + ' - click Login first' : 'This pane has no card with a login');
      return Promise.resolve(null);
    }
    if(manual) autoPromptDeclined = false;
    var go = token ? Promise.resolve(true)
      : (autoPromptDeclined ? Promise.resolve(false)
        : ensureUnlocked('Enter your PIN to fill the login for ' + info.name + '. Asked once per cockpit session.').then(function(ok){ if(!ok) autoPromptDeclined = true; return ok; }));
    return go.then(function(ok){
      if(!ok || !token){ if(manual) toast('Vault locked - not filled'); return null; }
      return api('fill', { cardId: info.cardId, url: url }).then(function(r){
        if(r.__status === 200){ if(manual) toast('Filling the ' + info.name + ' login on ' + hostOf(url)); return r; }
        if(r.__status === 401) setToken('');
        if(r.__status === 403 && (manual || !refusedOnce[info.cardId + '|' + hostOf(url)])){
          refusedOnce[info.cardId + '|' + hostOf(url)] = 1;
          toast('Blocked: origin mismatch - this page is on ' + hostOf(url) + ', not allowed for ' + info.name + '. Add it under Login, "Also fill on these sites".');
        }
        return null;
      });
    });
  }

  /* WV2: the host calls this when a pane finishes loading. It re-checks the pane's origin before injecting. */
  /* n: fields filled right away; -1 = the pane is not on that site (host check). */
  var lastGood = {}, frameRefused = {};
  function fillOutcome(slot, n, where){
    var info = paneInfo(slot) || { name: 'this card' };
    if(n > 0){ lastGood[slot] = Date.now(); toast('Filled the ' + info.name + ' login' + (where ? ' on ' + where : '')); }
    else if(n === -1) toast('Blocked: origin mismatch - the pane is not on ' + (where || 'the approved site'));
    else {
      /* The sign-in box may sit in a frame (e.g. Chase): wait for a frame result before saying nothing was found. */
      var t0 = Date.now();
      setTimeout(function(){
        if((lastGood[slot] || 0) >= t0 - 200) return;
        toast('No login fields found on ' + (where || 'this page') + ' yet - still watching for them');
      }, 1200);
    }
  }
  window.__adcVaultFillResult = function(slot, n, where){ fillOutcome(slot, typeof n === 'number' ? n : parseInt(n, 10) || 0, where || ''); };
  function postFill(slot, url, d, report, inFrame){
    try { host.postMessage({ type: 'adc-vault-fill', slot: slot, report: !!report, frame: !!inFrame, url: String(url || ''), data: { u: d.u || '', p: d.p || '', x: d.x || '', xl: d.xl || '', autoSubmit: !!d.autoSubmit, autoAdvance: !!d.autoAdvance } }); } catch(_){}
  }
  window.__adcVaultPaneLoaded = function(slot, url, manual, frames){
    if(!host || typeof slot !== 'number') return;
    if(manual) hostAnswered = true;
    fetchFill(slot, String(url || ''), !!manual).then(function(d){
      if(d){ postFill(slot, url, d, !!manual); d = null; }
      if(frames && frames.length) frameFills(slot, frames, !!manual);
    });
  };
  /* v1.5.1 frames: the vault decides per frame URL (card host or allowed sites); the host injects only into
     frames on exactly that origin. Unapproved frames (ads, widgets) are skipped quietly and remembered. */
  function frameFills(slot, urls, manual){
    var info = paneInfo(slot);
    if(!host || !token || !info || !info.cardId || !ids[info.cardId] || !urls || !urls.length) return;
    var seen = {};
    urls.slice(0, 16).forEach(function(u){
      u = String(u || '');
      var o; try { var x = new URL(u); if(x.protocol !== 'https:') return; o = x.origin; } catch(_){ return; }
      var k = info.cardId + '|' + o;
      if(seen[o] || frameRefused[k]) return;
      seen[o] = 1;
      api('fill', { cardId: info.cardId, url: u }).then(function(r){
        if(r.__status === 200) postFill(slot, u, r, manual, true);
        else if(r.__status === 403 || r.__status === 404) frameRefused[k] = 1;
        r = null;
      });
    });
  }
  window.__adcVaultFrameLoaded = function(slot, url){
    if(typeof slot !== 'number') return;
    frameFills(slot, [String(url || '')], false);
  };

  /* ---------- manual fill (Fill login button, Ctrl+Shift+L) ---------- */
  var lastSlot = null, hostAnswered = false;
  function manualFill(slot){
    if(typeof slot !== 'number' || isNaN(slot)) slot = pickSlot();
    if(slot == null){ toast('Click a pane first, then press Fill login'); return; }
    lastSlot = slot;
    if(!WV2){
      var frame = document.querySelector('.lp-slot[data-slot="' + slot + '"] iframe.lp-pane-frame');
      var url = frame ? frameUrl(frame) : '';
      if(!url){ toast('This pane has no page the vault can fill'); return; }
      fetchFill(slot, url, true).then(function(d){
        if(!d) return;
        var n = 0;
        try { d.force = true; n = adcVaultFill(frame.contentWindow, frame.contentDocument, d); } catch(_){}
        d = null;
        fillOutcome(slot, n, hostOf(url));
      });
      return;
    }
    /* WV2: newer hosts answer adc-vault-fill-request with the pane's real URL (__adcVaultPaneLoaded(slot, url, true)).
       Older hosts ignore it, so also offer the login for each exact allowed origin; the host injects only
       into the pane whose current origin equals the approved one. */
    hostAnswered = false;
    try { host.postMessage({ type: 'adc-vault-fill', request: true, slot: slot }); } catch(_){}
    setTimeout(function(){
      if(hostAnswered) return;
      var info = paneInfo(slot);
      if(!info || !info.cardId || !ids[info.cardId]){ fetchFill(slot, '', true); return; }
      ensureUnlocked('Enter your PIN to fill the login for ' + info.name + '.').then(function(ok){
        if(!ok){ toast('Vault locked - not filled'); return; }
        api('origins', { cardId: info.cardId }).then(function(o){
          var list = (o && o.origins) || [];
          if(!list.length){ toast('No saved login for ' + info.name); return; }
          toast('Filling the ' + info.name + ' login if this pane is on ' + list.map(hostOf).join(' or '));
          list.forEach(function(origin){
            api('fill', { cardId: info.cardId, url: origin + '/' }).then(function(r){
              if(r.__status === 200) postFill(slot, origin + '/', r);
              r = null;
            });
          });
        });
      });
    }, 600);
  }
  function pickSlot(){
    if(lastSlot != null && document.querySelector('.lp-slot[data-slot="' + lastSlot + '"] [data-login-slot]')) return lastSlot;
    var btns = document.querySelectorAll('.lp-pane-h [data-login-slot]');
    for(var i = 0; i < btns.length; i++){
      var s = parseInt(btns[i].getAttribute('data-login-slot'), 10);
      var inf = paneInfo(s);
      if(inf && inf.cardId && ids[inf.cardId]) return s;
    }
    return btns.length ? parseInt(btns[0].getAttribute('data-login-slot'), 10) : null;
  }
  window.__adcVaultManualFill = function(slot){ manualFill(typeof slot === 'number' ? slot : NaN); };
  function hotkey(e){
    if(e.ctrlKey && e.shiftKey && !e.altKey && (e.key === 'L' || e.key === 'l' || e.code === 'KeyL')){
      e.preventDefault(); e.stopPropagation();
      manualFill(NaN);
    }
  }
  document.addEventListener('keydown', hotkey, true);
  document.addEventListener('pointerdown', function(e){
    var sl = e.target && e.target.closest && e.target.closest('.lp-slot[data-slot]');
    if(sl) lastSlot = parseInt(sl.getAttribute('data-slot'), 10);
  }, true);

  /* Browser cockpit: panes are same-origin iframes at /frame/s/<host>/... (frame proxy). */
  function frameUrl(frame){
    try {
      var loc = frame.contentWindow.location;
      var m = /^\/frame\/s\/([^\/]+)(\/.*)?$/i.exec(loc.pathname || '');
      if(!m) return loc.protocol === 'https:' ? String(loc.href).split('#')[0] : '';
      var h = decodeURIComponent(m[1]);
      return 'https://' + h + (m[2] || '/') + (loc.search || '');
    } catch(_){ return ''; }
  }
  function watchFrame(frame){
    if(frame.__adcVault) return;
    frame.__adcVault = 1;
    function onLoad(){
      try {
        var fd = frame.contentDocument;
        if(fd && !fd.__adcVaultKeys){
          fd.__adcVaultKeys = 1;
          fd.addEventListener('keydown', function(e){
            var se = frame.closest('.lp-slot'); if(se) lastSlot = parseInt(se.getAttribute('data-slot'), 10);
            hotkey(e);
          }, true);
          fd.addEventListener('pointerdown', function(){ var se = frame.closest('.lp-slot'); if(se) lastSlot = parseInt(se.getAttribute('data-slot'), 10); }, true);
        }
      } catch(_){}
      var url = frameUrl(frame);
      if(!url) return;
      var slotEl = frame.closest('.lp-slot');
      var slot = slotEl ? parseInt(slotEl.getAttribute('data-slot'), 10) : NaN;
      if(isNaN(slot)) return;
      fetchFill(slot, url).then(function(d){
        if(!d) return;
        if(frameUrl(frame) !== url) return;
        var n = 0;
        try { n = adcVaultFill(frame.contentWindow, frame.contentDocument, d); } catch(_){}
        d = null;
        fillOutcome(slot, n, hostOf(url));
      });
    }
    frame.addEventListener('load', onLoad);
    try { if(frame.contentDocument && frame.contentDocument.readyState === 'complete' && frameUrl(frame)) onLoad(); } catch(_){}
  }

  /* ---------- buttons ---------- */
  function paintButtons(){
    document.querySelectorAll('.lp-pane-h [data-login-slot]').forEach(function(b){
      var info = paneInfo(parseInt(b.getAttribute('data-login-slot'), 10));
      var has = !!(info && info.cardId && ids[info.cardId]);
      b.classList.toggle('adc-vault-has', has);
      b.title = has ? 'Saved login (vault ' + (token ? 'unlocked' : 'locked') + '). Click to edit.' : 'Save a login for this card';
    });
  }
  function addButtons(){
    document.querySelectorAll('.lp-pane-h [data-detach-slot]').forEach(function(det){
      var hdr = det.parentElement;
      if(!hdr || hdr.querySelector('[data-login-slot]')) return;
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = 'Login';
      b.setAttribute('data-login-slot', det.getAttribute('data-detach-slot'));
      hdr.insertBefore(b, det);
      var fb = document.createElement('button');
      fb.type = 'button';
      fb.textContent = 'Fill login';
      fb.title = 'Fill this card\'s saved login into the page now (Ctrl+Shift+L)';
      fb.setAttribute('data-fill-slot', det.getAttribute('data-detach-slot'));
      hdr.insertBefore(fb, det);
      [['u', 'Copy user', 'Copy the saved username to the clipboard'], ['p', 'Copy pass', 'Copy the saved password to the clipboard (clears after 30 s)']].forEach(function(cfg){
        var cb = document.createElement('button');
        cb.type = 'button'; cb.textContent = cfg[1]; cb.title = cfg[2];
        cb.setAttribute('data-copy-slot', det.getAttribute('data-detach-slot'));
        cb.setAttribute('data-copy-field', cfg[0]);
        hdr.insertBefore(cb, det);
      });
    });
    if(!WV2) document.querySelectorAll('iframe.lp-pane-frame').forEach(watchFrame);
    paintButtons();
  }
  var headBtn = null;
  function paintHeader(){
    if(!headBtn){
      var tools = document.querySelector('header .adc-head-tools') || document.querySelector('header');
      if(!tools) return;
      headBtn = document.createElement('button');
      headBtn.type = 'button';
      headBtn.className = 'adc-vault-head';
      headBtn.addEventListener('click', function(){ if(token) lockVault(); else ensureUnlocked(); });
      tools.insertBefore(headBtn, tools.firstChild);
    }
    headBtn.textContent = token ? 'Lock vault' : 'Vault';
    headBtn.classList.toggle('unlocked', !!token);
    headBtn.title = token ? 'Vault is unlocked for this session. Click to lock it now.' : 'Vault is locked. Click to unlock (or set your PIN the first time).';
  }

  document.addEventListener('click', function(e){
    var cbtn = e.target && e.target.closest && e.target.closest('[data-copy-slot]');
    if(cbtn){
      e.preventDefault(); e.stopPropagation();
      copySlot(parseInt(cbtn.getAttribute('data-copy-slot'), 10), cbtn.getAttribute('data-copy-field'));
      return;
    }
    var fb = e.target && e.target.closest && e.target.closest('[data-fill-slot]');
    if(fb){
      e.preventDefault(); e.stopPropagation();
      manualFill(parseInt(fb.getAttribute('data-fill-slot'), 10));
      return;
    }
    var b = e.target && e.target.closest && e.target.closest('[data-login-slot]');
    if(!b) return;
    e.preventDefault();
    e.stopPropagation();
    openLogin(parseInt(b.getAttribute('data-login-slot'), 10));
  }, true);

  var queued = false;
  function queue(){ if(queued) return; queued = true; requestAnimationFrame(function(){ queued = false; addButtons(); }); }
  function toast(msg){
    var t = document.getElementById('lpToast');
    if(!t) return;
    t.textContent = msg;
    t.classList.add('show');
    clearTimeout(toast._t);
    toast._t = setTimeout(function(){ t.classList.remove('show'); }, 2200);
  }

  function start(){
    new MutationObserver(queue).observe(document.body, { childList: true, subtree: true });
    addButtons();
    paintHeader();
    refreshStatus();
  }
  if(document.body) start(); else document.addEventListener('DOMContentLoaded', start);

  /* End only this window's session when the cockpit page goes away. The key is wiped when no session is left. */
  window.addEventListener('pagehide', function(){
    if(!token) return;
    try {
      nativeFetch(BASE + '/adc-vault/end', { method: 'POST', keepalive: true, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: token }), credentials: 'omit', mode: 'same-origin' });
    } catch(_){}
    token = '';
  });
  window.__adcVaultLock = lockVault;
})();
