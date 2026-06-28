window.knowPulse = window.knowPulse || {};

/**
 * Reads a .NET stream reference and triggers a browser download without keeping the file in Blazor component state.
 *
 * @param {string} fileName Browser download file name returned by the backend.
 * @param {string} contentType MIME type returned by the backend.
 * @param {{ arrayBuffer: () => Promise<ArrayBuffer> }} contentStreamReference Stream reference supplied by Blazor.
 */
window.knowPulse.downloadFileFromStream = async function (fileName, contentType, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType || "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName || "download";
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

/**
 * Reads a .NET stream reference, verifies that its type is safe to preview, and opens it in a temporary browser Blob URL.
 *
 * @param {string} fileName File name used for extension-based preview decisions.
 * @param {string} contentType MIME type returned by the backend.
 * @param {{ arrayBuffer: () => Promise<ArrayBuffer> }} contentStreamReference Stream reference supplied by Blazor.
 */
window.knowPulse.previewFileFromStream = async function (fileName, contentType, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const previewContentType = getPreviewContentType(fileName, contentType);

    if (!previewContentType) {
        alert("该文件类型暂不支持在线预览，请下载后查看。");
        return;
    }

    const blob = new Blob([arrayBuffer], { type: previewContentType });
    const url = URL.createObjectURL(blob);
    const openedWindow = window.open(url, "_blank", "noopener,noreferrer");

    if (!openedWindow) {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.target = "_blank";
        anchor.rel = "noopener noreferrer";
        anchor.click();
        anchor.remove();
    }

    window.setTimeout(() => URL.revokeObjectURL(url), 60 * 1000);
};

/**
 * Chooses the browser preview MIME type for a file while excluding active content such as HTML, XML, and SVG.
 *
 * @param {string} fileName File name used as a fallback when content type is missing or generic.
 * @param {string} contentType Raw MIME type returned by the backend.
 * @returns {string | null} Safe preview MIME type, or null when the file must be downloaded instead.
 */
function getPreviewContentType(fileName, contentType) {
    const normalizedName = (fileName || "").toLowerCase();
    const normalizedType = (contentType || "").toLowerCase().split(";")[0].trim();

    if (normalizedType === "application/pdf" || normalizedName.endsWith(".pdf")) {
        return "application/pdf";
    }

    if (isSafeTextPreview(normalizedName, normalizedType)) {
        return "text/plain;charset=utf-8";
    }

    if (normalizedType.startsWith("image/") && normalizedType !== "image/svg+xml") {
        return normalizedType;
    }

    if (normalizedType.startsWith("audio/") || normalizedType.startsWith("video/")) {
        return normalizedType;
    }

    return null;
}

/**
 * Determines whether a file can be rendered as plain text without exposing executable browser content.
 *
 * @param {string} fileName Normalized lower-case file name.
 * @param {string} contentType Normalized lower-case MIME type.
 * @returns {boolean} True when the file can be previewed through a text/plain Blob.
 */
function isSafeTextPreview(fileName, contentType) {
    if (contentType === "application/json") {
        return true;
    }

    const safeTextExtensions = [".txt", ".md", ".markdown", ".json", ".csv", ".log", ".yaml", ".yml"];
    if (safeTextExtensions.some(extension => fileName.endsWith(extension))) {
        return true;
    }

    return contentType.startsWith("text/") &&
        contentType !== "text/html" &&
        contentType !== "text/xml" &&
        contentType !== "image/svg+xml";
}
