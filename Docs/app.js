(() => {
  const OWNER = "ziard47";
  const REPO = "voltrex-quest";
  const API_URL = `https://api.github.com/repos/${OWNER}/${REPO}/releases/latest`;
  const RELEASES_URL = `https://github.com/${OWNER}/${REPO}/releases`;

  function formatBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return "Unknown size";
    }
    const mb = bytes / (1024 * 1024);
    if (mb >= 1) {
      return `${mb.toFixed(1)} MB`;
    }
    const kb = bytes / 1024;
    return `${kb.toFixed(1)} KB`;
  }

  function selectInstallerAsset(assets) {
    if (!Array.isArray(assets) || assets.length === 0) {
      return null;
    }

    const withUrls = assets.filter(
      (a) => a && typeof a.name === "string" && typeof a.browser_download_url === "string"
    );

    return (
      withUrls.find((a) => {
        const name = a.name.toLowerCase();
        return name.endsWith(".exe") && (name.includes("setup") || name.includes("installer")) && name.includes("x64");
      }) ||
      withUrls.find((a) => {
        const name = a.name.toLowerCase();
        return name.endsWith(".exe") && (name.includes("setup") || name.includes("installer"));
      }) ||
      withUrls.find((a) => a.name.toLowerCase().endsWith(".exe")) ||
      null
    );
  }

  function appendInlineMarkdown(parent, text) {
    const codeParts = text.split(/(`[^`]+`)/g);
    for (const part of codeParts) {
      if (part.startsWith("`") && part.endsWith("`") && part.length > 2) {
        const codeEl = document.createElement("code");
        codeEl.textContent = part.slice(1, -1);
        parent.appendChild(codeEl);
      } else {
        const cleanText = part.replace(/\*\*([^*]+)\*\*/g, "$1");
        parent.appendChild(document.createTextNode(cleanText));
      }
    }
  }

  function renderReleaseNotes(container, markdownText) {
    if (!container || !markdownText || !markdownText.trim()) {
      return;
    }

    container.textContent = "";
    const lines = markdownText.replace(/\r\n/g, "\n").split("\n");
    let currentList = null;

    const flushList = () => {
      if (currentList) {
        container.appendChild(currentList);
        currentList = null;
      }
    };

    for (const rawLine of lines) {
      const line = rawLine.trim();

      if (!line) {
        flushList();
        continue;
      }

      if (/^---+$/.test(line)) {
        flushList();
        continue;
      }

      if (line.startsWith("#")) {
        flushList();
        const headingText = line.replace(/^#+\s*/, "");
        const h4 = document.createElement("h4");
        h4.textContent = headingText;
        container.appendChild(h4);
        continue;
      }

      if (/^[-*]\s+/.test(line) || /^\d+\.\s+/.test(line)) {
        if (!currentList) {
          currentList = document.createElement("ul");
        }
        const item = document.createElement("li");
        const cleanItem = line.replace(/^([-*]|\d+\.)\s+/, "");
        appendInlineMarkdown(item, cleanItem);
        currentList.appendChild(item);
        continue;
      }

      flushList();
      const p = document.createElement("p");
      appendInlineMarkdown(p, line);
      container.appendChild(p);
    }

    flushList();
  }

  async function hydrateLatestRelease() {
    try {
      const response = await fetch(API_URL, {
        headers: {
          Accept: "application/vnd.github+json"
        }
      });

      if (!response.ok) {
        return;
      }

      const release = await response.json();
      if (!release || release.draft || !release.tag_name) {
        return;
      }

      const versionTag = release.tag_name;
      const installer = selectInstallerAsset(release.assets);
      const releaseUrl = release.html_url || RELEASES_URL;

      const versionBadge = document.querySelector("#hero-version-badge span");
      if (versionBadge) {
        versionBadge.textContent = versionTag;
      }

      const panelVersion = document.getElementById("panel-version");
      if (panelVersion) {
        panelVersion.textContent = versionTag;
      }

      const releaseTitle = document.getElementById("release-title");
      if (releaseTitle) {
        releaseTitle.textContent = release.name || `Voltrex Quest ${versionTag}`;
      }

      const releaseMeta = document.getElementById("release-meta");
      if (releaseMeta && release.published_at) {
        const publishedDate = new Date(release.published_at).toLocaleDateString("en-US", {
          year: "numeric",
          month: "short",
          day: "numeric"
        });
        releaseMeta.textContent = `Published ${publishedDate} · ${OWNER}/${REPO}`;
      }

      const releasePageLink = document.getElementById("release-page-link");
      if (releasePageLink) {
        releasePageLink.href = releaseUrl;
      }

      if (release.body) {
        const notesContainer = document.getElementById("release-notes-content");
        renderReleaseNotes(notesContainer, release.body);
      }

      if (installer) {
        const sizeText = formatBytes(installer.size);
        const downloadBtn = document.getElementById("download-primary-btn");
        const downloadLabel = document.getElementById("download-btn-label");
        const installerNote = document.querySelector("#installer-meta-line span");
        const panelAsset = document.getElementById("panel-asset");
        const panelSize = document.getElementById("panel-size");
        const releaseAssetSummary = document.getElementById("release-asset-summary");

        if (downloadBtn) {
          downloadBtn.href = installer.browser_download_url;
        }
        if (downloadLabel) {
          downloadLabel.textContent = `Download ${versionTag} Installer (${sizeText})`;
        }
        if (installerNote) {
          installerNote.textContent = `${installer.name} · ${sizeText} · Inno Setup for Windows x64`;
        }
        if (panelAsset) {
          panelAsset.textContent = installer.name;
        }
        if (panelSize) {
          panelSize.textContent = sizeText;
        }
        if (releaseAssetSummary) {
          releaseAssetSummary.textContent = "";
          const icon = document.createElement("i");
          icon.className = "fa-solid fa-file-shield";
          icon.setAttribute("aria-hidden", "true");
          const label = document.createElement("span");
          label.appendChild(document.createTextNode("Installer: "));
          const code = document.createElement("code");
          code.textContent = installer.name;
          label.appendChild(code);
          label.appendChild(document.createTextNode(` (${sizeText})`));
          releaseAssetSummary.appendChild(icon);
          releaseAssetSummary.appendChild(label);
        }
      }
    } catch {
      // Keep static fallback metadata when offline or before a release is published.
    }
  }

  function initMobileNav() {
    const toggle = document.getElementById("nav-toggle");
    const nav = document.getElementById("primary-nav");
    if (!toggle || !nav) {
      return;
    }

    const icon = toggle.querySelector("i");

    const setMenuOpen = (isOpen) => {
      nav.classList.toggle("is-open", isOpen);
      toggle.setAttribute("aria-expanded", String(isOpen));
      if (icon) {
        icon.className = isOpen ? "fa-solid fa-xmark" : "fa-solid fa-bars";
      }
    };

    toggle.addEventListener("click", () => {
      const nextOpen = !nav.classList.contains("is-open");
      setMenuOpen(nextOpen);
    });

    nav.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => setMenuOpen(false));
    });
  }

  document.addEventListener("DOMContentLoaded", () => {
    initMobileNav();
    hydrateLatestRelease();
  });
})();

