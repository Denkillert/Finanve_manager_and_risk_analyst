// ОАО «МАПИД» — интерактивность оболочки веб-клиента
(function () {
  "use strict";

  var body = document.body;
  var toggle = document.getElementById("sidebarToggle");
  var overlay = document.getElementById("sidebarOverlay");
  var sidebar = document.getElementById("sidebar");

  function setSidebar(open) {
    body.classList.toggle("sidebar-open", open);
    if (toggle) {
      toggle.setAttribute("aria-expanded", open ? "true" : "false");
    }
  }

  if (toggle) {
    toggle.addEventListener("click", function () {
      setSidebar(!body.classList.contains("sidebar-open"));
    });
  }

  if (overlay) {
    overlay.addEventListener("click", function () {
      setSidebar(false);
    });
  }

  if (sidebar) {
    sidebar.addEventListener("click", function (event) {
      if (event.target.closest("a.nav-link") && window.innerWidth <= 900) {
        setSidebar(false);
      }
    });
  }

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape") {
      setSidebar(false);
    }
  });

  window.addEventListener("resize", function () {
    if (window.innerWidth > 900) {
      setSidebar(false);
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
