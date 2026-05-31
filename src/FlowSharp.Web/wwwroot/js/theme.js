window.FlowSharp = {
    get() {
        return localStorage.getItem("FlowSharp-theme") === "dark";
    },
    set(isDark) {
        localStorage.setItem("FlowSharp-theme", isDark ? "dark" : "light");
        document.documentElement.dataset.theme = isDark ? "dark" : "light";
    }
};
