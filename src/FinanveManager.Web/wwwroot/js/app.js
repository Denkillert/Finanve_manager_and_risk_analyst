// ОАО «МАПИД» — интерактивность оболочки веб-клиента:
// навигация «меню → раздел → меню», Esc как «Назад» и мягкие появления.
(function () {
  "use strict";

  // Esc возвращает в главное меню, но не мешает работе с полями ввода.
  document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape" || event.altKey || event.ctrlKey || event.metaKey) return;

    var target = event.target;
    if (target && target.closest) {
      if (target.closest("input, textarea, select, [contenteditable='true']")) return;
      if (target.closest("dialog[open], .modal, .popup")) return;
    }

    var back = document.querySelector("[data-back-link]");
    if (!back) return;

    var href = back.getAttribute("href");
    if (href) {
      event.preventDefault();
      window.location.href = href;
    }
  });

  // Тень верхней панели появляется только при прокрутке — интерфейс остаётся чистым.
  var bar = document.querySelector(".webbar");
  if (bar) {
    var sync = function () {
      bar.classList.toggle("is-scrolled", window.scrollY > 6);
    };
    window.addEventListener("scroll", sync, { passive: true });
    sync();
  }

  // Плавное скрытие информационных сообщений.
  document.querySelectorAll(".alert-success, .alert-info").forEach(function (alert) {
    window.setTimeout(function () {
      alert.style.transition = "opacity .4s ease, transform .4s ease";
      alert.style.opacity = "0";
      alert.style.transform = "translateY(-4px)";
      window.setTimeout(function () { alert.remove(); }, 420);
    }, 7000);
  });

  // Клавиатурная навигация по плиткам главного меню: стрелки двигают фокус.
  var tiles = Array.prototype.slice.call(document.querySelectorAll(".tiles .tile"));
  if (tiles.length > 0) {
    document.addEventListener("keydown", function (event) {
      if (event.key !== "ArrowRight" && event.key !== "ArrowLeft" &&
          event.key !== "ArrowDown" && event.key !== "ArrowUp") return;

      var index = tiles.indexOf(document.activeElement);
      if (index < 0) return;

      var step = (event.key === "ArrowRight" || event.key === "ArrowDown") ? 1 : -1;
      var next = tiles[(index + step + tiles.length) % tiles.length];
      if (!next) return;

      event.preventDefault();
      next.focus();
    });
  }
})();
