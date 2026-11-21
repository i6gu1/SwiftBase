const toast = document.getElementById("toast");
let toastTimer;

function showToast(message) {
  if (!toast) return;
  toast.textContent = message;
  toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("show"), 2600);
}

document.querySelectorAll("[data-download]").forEach((link) => {
  link.addEventListener("click", () => {
    showToast("Download started — thanks for using SwiftBase.");
  });
});

const copyButton = document.getElementById("copy-sha");
const shaElement = document.getElementById("sha");

copyButton?.addEventListener("click", async () => {
  const value = shaElement?.textContent?.trim() ?? "";
  try {
    await navigator.clipboard.writeText(value);
    showToast("SHA256 copied to clipboard.");
  } catch {
    showToast("Could not access clipboard.");
  }
});
