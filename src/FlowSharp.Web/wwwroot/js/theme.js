window.FlowSharp = {
    get() {
        return localStorage.getItem("FlowSharp-theme") === "dark";
    },
    set(isDark) {
        localStorage.setItem("FlowSharp-theme", isDark ? "dark" : "light");
        document.documentElement.dataset.theme = isDark ? "dark" : "light";
    },
    // Kayitli temayi <html data-theme>'e uygular. Inline head scripti ilk tam yuklemede,
    // bu ise her SPA gecisinde (asagidaki enhancedload) cagrilir.
    apply() {
        document.documentElement.dataset.theme =
            localStorage.getItem("FlowSharp-theme") === "dark" ? "dark" : "light";
    }
};

// Blazor "enhanced navigation" sayfalar arasi gecerken <html> ogesini sunucudaki haliyle
// birlestirir ve JS ile eklenen data-theme'i silebilir; bu da salt-CSS olan WorkflowDesigner'in
// gecislerde light'a donmesine yol acar. Her enhanced yukleme sonrasi temayi yeniden uygula.
if (window.Blazor) {
    Blazor.addEventListener("enhancedload", () => window.FlowSharp.apply());
}
