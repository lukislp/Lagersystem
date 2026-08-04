// Keyboard Shortcuts Handler
class KeyboardShortcutManager {
    constructor() {
        this.shortcuts = new Map();
        this.enabled = true;
        this.init();
    }

    init() {
        document.addEventListener("keydown", (e) => this.handleKeyDown(e));
    }

    register(keys, callback, description = "") {
        const normalizedKeys = this.normalizeKeys(keys);
        this.shortcuts.set(normalizedKeys, { callback, description });
    }

    unregister(keys) {
        const normalizedKeys = this.normalizeKeys(keys);
        this.shortcuts.delete(normalizedKeys);
    }

    normalizeKeys(keys) {
        // Normalize key combination (e.g., "Ctrl+S" or "ctrl+s")
        return keys
            .toLowerCase()
            .replace("control", "ctrl")
            .replace(" ", "")
            .split("+")
            .sort()
            .join("+");
    }

    handleKeyDown(e) {
        if (!this.enabled) return;

        // Don't trigger shortcuts when typing in inputs
        if (
            e.target.tagName === "INPUT" ||
            e.target.tagName === "TEXTAREA" ||
            e.target.isContentEditable
        ) {
            // Exception for Escape key
            if (e.key !== "Escape") return;
        }

        let keys = [];

        if (e.ctrlKey || e.metaKey) keys.push("ctrl");
        if (e.shiftKey) keys.push("shift");
        if (e.altKey) keys.push("alt");
        const mainKey = e.key.toLowerCase();
        if (!["control", "shift", "alt", "meta"].includes(mainKey)) {
            keys.push(mainKey);
        }

        const normalizedKeys = keys.sort().join("+");
        const shortcut = this.shortcuts.get(normalizedKeys);

        if (shortcut) {
            e.preventDefault();
            e.stopPropagation();
            shortcut.callback();
        }
    }

    enable() {
        this.enabled = true;
    }

    disable() {
        this.enabled = false;
    }

    getShortcuts() {
        return Array.from(this.shortcuts.entries()).map(([keys, data]) => ({
            keys,
            description: data.description,
        }));
    }
}

// Global instance
window.keyboardShortcuts = new KeyboardShortcutManager();

// DotNet interop
window.registerKeyboardShortcut = (
    keys,
    dotNetHelper,
    methodName,
    description,
) => {
    window.keyboardShortcuts.register(
        keys,
        () => {
            dotNetHelper.invokeMethodAsync(methodName);
        },
        description,
    );
};

window.unregisterKeyboardShortcut = (keys) => {
    window.keyboardShortcuts.unregister(keys);
};

window.enableKeyboardShortcuts = () => {
    window.keyboardShortcuts.enable();
};

window.disableKeyboardShortcuts = () => {
    window.keyboardShortcuts.disable();
};
