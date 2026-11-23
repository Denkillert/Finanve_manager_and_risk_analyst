using FinanveManager.Data.Services;

namespace FinanveManager.Web.Services;

/// <summary>Единый набор иконок разделов для верхней панели и плиток главного меню.</summary>
public static class SectionIcons
{
    public static string Svg(AppSection section) => section switch
    {
        AppSection.Dashboard => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"3\" y=\"3\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\" rx=\"1.6\"/></svg>",
        AppSection.ProjectsAndBudgets => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M3 21h18\"/><path d=\"M5 21V7l7-4 7 4v14\"/><path d=\"M9 21v-5h6v5\"/><path d=\"M9 10h.01M15 10h.01M9 13h.01M15 13h.01\"/></svg>",
        AppSection.PaymentRequests => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/><path d=\"M14 2v6h6\"/><path d=\"M8 13h8M8 17h5\"/></svg>",
        AppSection.PaymentCalendar => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"3\" y=\"4\" width=\"18\" height=\"17\" rx=\"2.4\"/><path d=\"M16 2v4M8 2v4M3 10h18\"/><path d=\"M8 14h.01M12 14h.01M16 14h.01M8 17.5h.01M12 17.5h.01\"/></svg>",
        AppSection.ContractsAndStatements => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M15 2H7a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V6z\"/><path d=\"M15 2v4h4\"/><path d=\"M9 12h6M9 16h4\"/><path d=\"M9 8h2\"/></svg>",
        AppSection.RisksAndP80 => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M12 22s8-3.6 8-10V5l-8-3-8 3v7c0 6.4 8 10 8 10z\"/><path d=\"M12 8v4\"/><path d=\"M12 16h.01\"/></svg>",
        AppSection.Reports => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M3 3v18h18\"/><rect x=\"7\" y=\"12\" width=\"3\" height=\"6\" rx=\"0.8\"/><rect x=\"12.5\" y=\"8\" width=\"3\" height=\"10\" rx=\"0.8\"/><rect x=\"18\" y=\"5\" width=\"3\" height=\"13\" rx=\"0.8\"/></svg>",
        AppSection.SiteActs => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M9 2h6a1 1 0 0 1 1 1v2H8V3a1 1 0 0 1 1-1z\"/><path d=\"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2\"/><path d=\"m8.5 14 2 2 4.5-4.5\"/></svg>",
        AppSection.Administration => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M22 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/></svg>",
        AppSection.AuditLog => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8\"/><path d=\"M3 3v5h5\"/><path d=\"M12 7v5l4 2\"/></svg>",
        _ => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"12\" cy=\"12\" r=\"9\"/></svg>"
    };

    /// <summary>Действие контроллера, открывающее раздел.</summary>
    public static string Action(AppSection section) => section switch
    {
        AppSection.AuditLog => "Audit",
        AppSection.SiteActs => "Acts",
        _ => "Index"
    };
}
