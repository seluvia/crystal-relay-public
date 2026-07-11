(function () {
    "use strict";

    if (window.__crLiveListInjected) return;
    window.__crLiveListInjected = true;

    /* ── CSS Cleanup ── */
    function applyCleanup() {
        var style = document.createElement("style");
        style.id = "cr-livelist-cleanup";
        style.textContent = [
            "[data-a-target=\"side-nav\"] { display: none !important; }",
            "[data-a-target=\"top-nav\"] { display: none !important; }",
            "[data-test-selector=\"recommended-section\"] { display: none !important; }",
            ".recommended-section, .recommended-show { display: none !important; }",
            "[data-a-target*=\"recommended\"] { display: none !important; }",
            "footer, .tw-footer { display: none !important; }",
            ".channel-root, .channel-root__content { max-width: none !important; margin: 0 !important; padding-left: 0 !important; }",
            ".video-player, .channel-info-content { max-width: none !important; }"
        ].join(" ");
        document.head.appendChild(style);
    }

    /* ── Theatre Mode ── */
    function tryTheatreMode() {
        var btn = document.querySelector('[data-a-target="player-theatre-mode-button"]');
        if (btn && btn.getAttribute("aria-pressed") !== "true") {
            btn.click();
        }
    }

    /* ── Channel Points Auto-Claim ── */
    function setupAutoClaim() {
        function getRandomDelay() {
            var arr = new Uint32Array(7);
            crypto.getRandomValues(arr);

            /* Base: 800–8000ms */
            var base = 800 + (arr[0] % 7201);

            /* Jitter 1: sinusoidal ±1500ms */
            var j1 = Math.sin(arr[1] * 0.0003) * 1500;

            /* Jitter 2: prime-multiplier 0–2296ms in steps of 177 */
            var j2 = (arr[2] % 13) * 177;

            /* Jitter 3: crypto modulo 693 × 0.7 + 50 */
            var j3 = (arr[6] % 693) * 0.7 + 50;

            /* Jitter 4: raw noise ±250ms */
            var j4 = (arr[3] % 500) - 250;

            /* Jitter 5: log-scaled 0~1200ms */
            var j5 = Math.log10(1 + (arr[4] % 1000)) * 400;

            /* Jitter 6: golden-ratio modulated 0~809ms */
            var j6 = (arr[5] % 1000) * 1.618033988749895 * 0.5;

            return Math.max(200, Math.floor(base + j1 + j2 + j3 + j4 + j5 + j6));
        }

        var claimTimer = null;
        var selectors = [
            '[data-a-target="claim-channel-points-button"]',
            '[data-a-target="bonus-points-button"]',
            '[data-test-selector="claim-points-button"]',
            'button[aria-label^="Claim "]'
        ];

        function tryClaim() {
            if (claimTimer !== null) return;
            for (var i = 0; i < selectors.length; i++) {
                var btn = document.querySelector(selectors[i]);
                if (btn && btn.getClientRects().length > 0 && !btn.disabled && !btn.dataset.crClaimed) {
                    btn.dataset.crClaimed = "true";
                    var delay = getRandomDelay();
                    claimTimer = setTimeout(function () {
                        if (!btn.isConnected) { claimTimer = null; return; }
                        try { btn.click(); } catch (_) { }
                        claimTimer = null;
                    }, delay);
                    return;
                }
            }
        }

        /* Initial attempt after page settles */
        setTimeout(tryClaim, 3000);

        /* Observe DOM for claim button appearing dynamically */
        var observer = new MutationObserver(function () { tryClaim(); });
        observer.observe(document.body, { childList: true, subtree: true });

        /* Fallback interval in case observer misses */
        setInterval(function () {
            if (claimTimer === null) tryClaim();
        }, 5000);
    }

    /* ── Initialize ── */
    function init() {
        applyCleanup();
        setTimeout(tryTheatreMode, 2000);
        setupAutoClaim();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
