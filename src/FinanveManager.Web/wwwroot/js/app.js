// ОАО «МАПИД» — интерактивность оболочки веб-клиента:
// навигация «меню → раздел → меню», Esc как «Назад» и мягкие появления.
(function () {
  "use strict";

  // Esc сворачивает раздел обратно в главное меню.
  document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape") return;
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
})();
