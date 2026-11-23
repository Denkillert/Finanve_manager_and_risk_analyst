// ОАО «МАПИД» — интерактивность оболочки веб-клиента:
// навигация «меню → раздел → меню» и плавное скрытие служебных сообщений.
(function () {
  "use strict";

  // Esc сворачивает раздел обратно в главное меню (кнопка «Назад»).
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

  // Плавное скрытие информационных сообщений
  document.querySelectorAll(".alert-success, .alert-info").forEach(function (alert) {
    window.setTimeout(function () {
      alert.style.transition = "opacity .4s ease, transform .4s ease";
      alert.style.opacity = "0";
      alert.style.transform = "translateY(-4px)";
      window.setTimeout(function () { alert.remove(); }, 420);
    }, 7000);
  });
})();
