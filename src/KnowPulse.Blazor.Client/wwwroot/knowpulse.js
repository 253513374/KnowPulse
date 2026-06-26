window.knowPulse = window.knowPulse || {};

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
