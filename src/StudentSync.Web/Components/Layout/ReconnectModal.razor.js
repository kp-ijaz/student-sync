// Reconnect behaviour for the Blazor Server connection (ReconnectModal.razor).
// Phones drop the connection whenever the screen locks or the app goes to the background. The dialog opens at once (so taps
// wait instead of being lost) but stays invisible for a moment: quick reconnects show nothing. After that a shimmer sweeps
// over the page. A paused session is resumed automatically and the page reloads when the server no longer has the session.
// Only a real offline state shows a card with Try again.
const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);
document.getElementById("components-reconnect-button").addEventListener("click", retry);

const QUIET_MS = 1200;
let revealTimer = 0;

function open() {
    if (!reconnectModal.open) reconnectModal.showModal();
    clearTimeout(revealTimer);
    revealTimer = setTimeout(() => reconnectModal.classList.add("reconnect-visible"), QUIET_MS);
}

function close() {
    clearTimeout(revealTimer);
    reconnectModal.classList.remove("reconnect-visible");
    if (reconnectModal.open) reconnectModal.close();
}

function handleReconnectStateChanged(event) {
    const state = event.detail.state;
    if (state === "show") {
        open();
    } else if (state === "hide") {
        close();
    } else if (state === "failed") {
        // Out of automatic attempts (offline): show the card now, and try again as soon as the page is visible again.
        open();
        reconnectModal.classList.add("reconnect-visible");
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        if (document.visibilityState === "visible") setTimeout(retry, 1500);
    } else if (state === "paused") {
        open();
        resume();
    } else if (state === "rejected" || state === "resume-failed") {
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    try {
        // true = reconnected; false = the server is reachable but no longer has this session.
        const successful = await Blazor.reconnect();
        if (!successful) await resume();
    } catch {
        // Still offline: wait for the page to become visible again or a tap on Try again.
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        if (await Blazor.resumeCircuit()) close();
        else location.reload();
    } catch {
        location.reload();
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") await retry();
}
