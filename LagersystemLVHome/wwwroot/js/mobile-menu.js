// Mobile menu toggle for the sidebar
window.mobileMenu = {
    toggle: function () {
        const sidebar = document.querySelector(".sidebar");
        const body = document.body;

        if (sidebar) {
            sidebar.classList.toggle("open");

            // Backdrop for the mobile menu
            if (sidebar.classList.contains("open")) {
                const backdrop = document.createElement("div");
                backdrop.className = "mobile-backdrop";
                backdrop.onclick = () => this.close();
                body.appendChild(backdrop);
                body.style.overflow = "hidden";
            } else {
                this.close();
            }
        }
    },

    close: function () {
        const sidebar = document.querySelector(".sidebar");
        const backdrop = document.querySelector(".mobile-backdrop");
        const body = document.body;

        if (sidebar) {
            sidebar.classList.remove("open");
        }

        if (backdrop) {
            backdrop.remove();
        }

        body.style.overflow = "";
    },
};

// Auto-close sidebar on navigation (mobile)
document.addEventListener("DOMContentLoaded", function () {
    // Close sidebar when a nav item is clicked (mobile only)
    if (window.innerWidth <= 768) {
        document.addEventListener("click", function (e) {
            if (e.target.closest(".nav-item")) {
                window.mobileMenu.close();
            }
        });
    }
    let resizeTimer;
    window.addEventListener("resize", function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            if (window.innerWidth > 768) {
                window.mobileMenu.close();
            }
        }, 250);
    });
});

// Swipe to close sidebar (Touch Gesture)
let touchStartX = 0;
let touchEndX = 0;

document.addEventListener(
    "touchstart",
    function (e) {
        touchStartX = e.changedTouches[0].screenX;
    },
    false,
);

document.addEventListener(
    "touchend",
    function (e) {
        touchEndX = e.changedTouches[0].screenX;
        handleSwipe();
    },
    false,
);

function handleSwipe() {
    const sidebar = document.querySelector(".sidebar");

    if (!sidebar || !sidebar.classList.contains("open")) return;

    // Swipe left to close (mindestens 50px)
    if (touchStartX - touchEndX > 50) {
        window.mobileMenu.close();
    }
}

// Viewport height fix for mobile browsers (100vh issue)
function setVH() {
    let vh = window.innerHeight * 0.01;
    document.documentElement.style.setProperty("--vh", `${vh}px`);
}

window.addEventListener("load", setVH);
window.addEventListener("resize", setVH);

// Prevent zoom on double-tap (iOS Safari)
let lastTouchEnd = 0;
document.addEventListener(
    "touchend",
    function (event) {
        const now = new Date().getTime();
        if (now - lastTouchEnd <= 300) {
            event.preventDefault();
        }
        lastTouchEnd = now;
    },
    false,
);

// Disable pull-to-refresh (prevents accidental reloads)
document.body.style.overscrollBehavior = "contain";
document.documentElement.style.setProperty(
    "--viewport-height",
    `${window.innerHeight}px`,
);
