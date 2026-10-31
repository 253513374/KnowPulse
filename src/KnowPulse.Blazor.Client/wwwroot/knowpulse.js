window.knowPulse = window.knowPulse || {};

window.knowPulse.answerImageDrafts = (() => {
    const databaseName = "knowpulse-assessment-image-drafts-v1";
    const storeName = "pending";
    const lifetimeMs = 24 * 60 * 60 * 1000;

    async function transact(mode, work) {
        let connection;
        const database = await new Promise((resolve, reject) => {
            let finished = false;
            const timer = setTimeout(() => { finished = true; reject(new Error("Image cache open timed out")); }, 5000);
            const request = indexedDB.open(databaseName, 1);
            request.onupgradeneeded = () => request.result.createObjectStore(storeName);
            request.onerror = request.onblocked = () => { finished = true; clearTimeout(timer); reject(new Error("Image cache unavailable")); };
            request.onsuccess = () => {
                clearTimeout(timer);
                if (finished) { request.result.close(); return; }
                connection = request.result;
                connection.onversionchange = () => connection.close();
                resolve(connection);
            };
        });
        try {
            return await new Promise((resolve, reject) => {
                const transaction = database.transaction(storeName, mode);
                let result;
                const timer = setTimeout(() => { try { transaction.abort(); } catch {} reject(new Error("Image cache transaction timed out")); }, 5000);
                transaction.oncomplete = () => { clearTimeout(timer); resolve(result); };
                transaction.onerror = transaction.onabort = () => { clearTimeout(timer); reject(new Error("Image cache transaction failed")); };
                try { work(transaction.objectStore(storeName), value => { result = value; }); }
                catch (error) { try { transaction.abort(); } catch {} clearTimeout(timer); reject(error); }
            });
        } finally { database.close(); }
    }

    function key(scope, operationId) {
        if (typeof scope !== "string" || scope.length > 256 || !scope || !/^[a-f0-9-]{36}$/i.test(operationId)) throw new Error("Invalid image cache key");
        return [scope, operationId];
    }

    return {
        async put(scope, operationId, bytes) {
            if (!(bytes instanceof Uint8Array) || !bytes.length || bytes.length > 20 * 1024 * 1024) throw new Error("Invalid image cache bytes");
            const id = key(scope, operationId);
            await transact("readwrite", store => {
                const request = store.get(id);
                request.onsuccess = () => store.put({
                    bytes: new Blob([bytes], { type: "application/octet-stream" }),
                    expiresAt: request.result?.expiresAt > Date.now() ? request.result.expiresAt : Date.now() + lifetimeMs
                }, id);
            });
        },
        async get(scope, operationId) {
            const id = key(scope, operationId);
            const bytes = await transact("readwrite", (store, result) => {
                const request = store.get(id);
                request.onsuccess = () => {
                    if (request.result?.expiresAt > Date.now()) result(request.result.bytes);
                    else if (request.result) store.delete(id);
                };
            });
            return bytes ? DotNet.createJSStreamReference(bytes) : null;
        },
        async remove(scope, operationId) {
            const id = key(scope, operationId);
            await transact("readwrite", store => store.delete(id));
        },
        async cleanupExpired(accountPrefix) {
            if (typeof accountPrefix !== "string" || !accountPrefix || accountPrefix.length > 256) throw new Error("Invalid image cache account");
            await transact("readwrite", store => {
                const request = store.openCursor(IDBKeyRange.bound([accountPrefix], [accountPrefix + "\uffff"]));
                request.onsuccess = () => {
                    const cursor = request.result;
                    if (!cursor) return;
                    if (cursor.value.expiresAt <= Date.now()) cursor.delete();
                    cursor.continue();
                };
            });
        }
    };
})();

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
