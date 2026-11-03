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
