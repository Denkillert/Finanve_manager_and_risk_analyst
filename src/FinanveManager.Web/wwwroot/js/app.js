// ОАО «МАПИД» — интерактивность оболочки веб-клиента:
// главное меню на плитках, переход «плитка раскрывается в раздел»
// и обратное сворачивание раздела в меню по кнопке «Назад» или Esc.
(function () {
  "use strict";

  var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var TILE_KEY = "fm:last-tile";
  var DIR_KEY = "fm:direction";
  var DUR = reduceMotion ? 0 : 340;
  var EASE_OUT = "cubic-bezier(0.22, 0.8, 0.26, 1)";
  var EASE_IN = "cubic-bezier(0.6, 0.02, 0.78, 0.2)";

  var stage = document.querySelector(".content.stage");
  var isHub = document.body.classList.contains("page-hub");

  function read(key) {
    try { return window.sessionStorage.getItem(key); } catch (e) { return null; }
  }

  function write(key, value) {
    try { window.sessionStorage.setItem(key, value); } catch (e) { /* приватный режим */ }
  }

  function drop(key) {
    try { window.sessionStorage.removeItem(key); } catch (e) { /* приватный режим */ }
  }

  function storedTile() {
    var raw = read(TILE_KEY);
    if (!raw) return null;
    try { return JSON.parse(raw); } catch (e) { return null; }
  }

  // Документные координаты плитки: не зависят от прокрутки страницы.
  function rememberTile(tile, href) {
    var box = tile.getBoundingClientRect();
    var style = window.getComputedStyle(tile);
    var accent = (style.getPropertyValue("--tile-accent") || "").trim() || "#0e7c66";
    var soft = (style.getPropertyValue("--tile-soft") || "").trim() || "#e6f5f1";
    var target = href;
    try { target = new URL(href, window.location.href).pathname; } catch (e) { /* оставляем как есть */ }
    write(TILE_KEY, JSON.stringify({
      path: target,
      left: box.left,
      top: box.top + window.scrollY,
      width: box.width,
      height: box.height,
      accent: accent,
      soft: soft
    }));
  }

  function stageRect() {
    if (!stage) return null;
    var box = stage.getBoundingClientRect();
    return { left: box.left, top: box.top, width: box.width, height: box.height };
  }

  function makeGhost(rect, tile) {
    var ghost = document.createElement("div");
    ghost.className = "nav-ghost";
    ghost.style.left = rect.left + "px";
    ghost.style.top = rect.top + "px";
    ghost.style.width = rect.width + "px";
    ghost.style.height = rect.height + "px";
    if (tile) {
      ghost.style.borderColor = tile.accent;
      ghost.style.background =
        "linear-gradient(135deg, " + tile.soft + " 0%, #ffffff 62%)";
      ghost.style.boxShadow =
        "0 22px 56px rgba(16, 32, 30, 0.2), 0 0 0 1px " + tile.soft + " inset";
    }
    document.body.appendChild(ghost);
    return ghost;
  }

  function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }

  // Раскрытие: плитка превращается в прямоугольник рабочей области.
  function expandIntoTile(tileEl, href) {
    var tile = storedTile();
    var from = tileEl.getBoundingClientRect();
    var to = stageRect();
    if (!to) { window.location.href = href; return; }

    var ghost = makeGhost(from, tile);
    var scaleX = clamp(to.width / from.width, 0.05, 20);
    var scaleY = clamp(to.height / from.height, 0.05, 20);
    var dx = to.left - from.left;
    var dy = to.top - from.top;

    var navigated = false;
    function go() {
      if (navigated) return;
      navigated = true;
      window.location.href = href;
    }

    document.documentElement.classList.add("nav-busy");
    if (stage) stage.classList.add("nav-forward");

    var animation = ghost.animate([
      { transform: "translate(0, 0) scale(1, 1)", opacity: 1 },
      { transform: "translate(" + dx + "px, " + dy + "px) scale(" + scaleX + ", " + scaleY + ")", opacity: 1 }
    ], { duration: DUR, easing: EASE_OUT, fill: "forwards" });

    animation.finished.then(go).catch(go);
    window.setTimeout(go, DUR + 450);
  }

  // Сворачивание: раздел сжимается обратно в свою плитку меню.
  function collapseIntoHub(href) {
    var tile = storedTile();
    if (!tile || !stage || window.location.pathname !== tile.path) {
      window.location.href = href;
      return;
    }

    var box = stage.getBoundingClientRect();
    var originX = ((tile.left - window.scrollX) + tile.width / 2 - box.left) / box.width * 100;
    var originY = ((tile.top - window.scrollY) + tile.height / 2 - box.top) / box.height * 100;
    var scaleX = clamp(tile.width / box.width, 0.04, 1);
    var scaleY = clamp(tile.height / box.height, 0.04, 1);

    var navigated = false;
    function go() {
      if (navigated) return;
      navigated = true;
      window.location.href = href;
    }

    document.documentElement.classList.add("nav-busy");
    write(DIR_KEY, "back");

    stage.style.transformOrigin =
      clamp(originX, -40, 140) + "% " + clamp(originY, -40, 140) + "%";

    var animation = stage.animate([
      { transform: "none", opacity: 1, borderRadius: "0px" },
      {
        transform: "scale(" + scaleX + ", " + scaleY + ")",
        opacity: 0.35,
        borderRadius: "18px"
      }
    ], {
      duration: DUR,
      easing: EASE_IN,
      fill: "forwards"
    });

    animation.finished.then(go).catch(go);
    window.setTimeout(go, DUR + 450);
  }

  // Приход в раздел: поверх рабочей области тает «раскрывшаяся плитка».
  function playArrival() {
    if (!stage) return;
    var tile = storedTile();
    var direction = read(DIR_KEY);
    if (!tile || direction === "back") return;

    if (!reduceMotion && window.location.pathname === tile.path) {
      var to = stageRect();
      if (to) {
        var ghost = makeGhost(to, tile);
        var fade = ghost.animate([
          { opacity: 1 },
          { opacity: 0 }
        ], { duration: 300, delay: 60, easing: "ease-out", fill: "forwards" });
        var cleanup = function () { ghost.remove(); };
        fade.finished.then(cleanup).catch(cleanup);
        window.setTimeout(cleanup, 900);
      }
    }

    stage.classList.add("nav-enter");
    window.setTimeout(function () { stage.classList.remove("nav-enter"); }, 1000);
  }

  // Возврат в меню: шапка и плитки проявляются заново.
  function playComeback() {
    if (!isHub || !stage) return;
    if (read(DIR_KEY) !== "back") return;
    drop(DIR_KEY);
    stage.classList.add("nav-back");
    window.setTimeout(function () { stage.classList.remove("nav-back"); }, 900);
  }

  // Клик по плитке главного меню — раскрываем её в раздел.
  document.addEventListener("click", function (event) {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (!event.target || !event.target.closest) return;

    var tile = event.target.closest("a.tile");
    if (!tile) return;
    var href = tile.getAttribute("href");
    if (!href || href.charAt(0) === "#") return;
    if (document.documentElement.classList.contains("nav-busy")) {
      event.preventDefault();
      return;
    }

    event.preventDefault();
    rememberTile(tile, href);
    write(DIR_KEY, "forward");

    if (reduceMotion) { window.location.href = href; return; }
    expandIntoTile(tile, href);
  });

  // «Назад» и Esc сворачивают раздел в главное меню.
  document.addEventListener("click", function (event) {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (!event.target || !event.target.closest) return;

    var back = event.target.closest("[data-back-link]");
    if (!back) return;
    var href = back.getAttribute("href");
    if (!href) return;
    if (document.documentElement.classList.contains("nav-busy")) {
      event.preventDefault();
      return;
    }

    event.preventDefault();
    if (reduceMotion) { window.location.href = href; return; }
    collapseIntoHub(href);
  });

  document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape") return;
    var back = document.querySelector("[data-back-link]");
    if (!back) return;
    var href = back.getAttribute("href");
    if (!href) return;
    event.preventDefault();
    if (document.documentElement.classList.contains("nav-busy")) return;
    if (reduceMotion) { window.location.href = href; return; }
    collapseIntoHub(href);
  });

  // Плавное скрытие информационных сообщений.
  document.querySelectorAll(".alert-success, .alert-info").forEach(function (alert) {
    window.setTimeout(function () {
      alert.style.transition = "opacity .4s ease, transform .4s ease";
      alert.style.opacity = "0";
      alert.style.transform = "translateY(-4px)";
      window.setTimeout(function () { alert.remove(); }, 420);
    }, 7000);
  });

  playArrival();
  playComeback();
})();
