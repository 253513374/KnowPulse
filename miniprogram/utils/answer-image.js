const { imagePath } = require('./answer-store');
const error = (code) => Object.assign(new Error(code), { code });
const call = (target, method, options) => new Promise((resolve, reject) => target[method]({ ...options, success: resolve, fail: () => reject(error('IMAGE_UNAVAILABLE')) }));
async function inspect(platform, filePath, policy) {
  if (!policy || !Number.isSafeInteger(policy.maxFileBytes) || policy.maxFileBytes < 1 || !Array.isArray(policy.allowedContentTypes) || typeof platform.getFileInfo !== 'function') throw error('IMAGE_UNAVAILABLE');
  const info = await call(platform, 'getFileInfo', { filePath, digestAlgorithm: 'sha256' });
  if (!Number.isSafeInteger(info.size) || info.size < 12 || info.size > policy.maxFileBytes || !/^[a-f0-9]{64}$/i.test(info.digest)) throw error('IMAGE_INVALID');
  const file = await call(platform.getFileSystemManager(), 'readFile', { filePath, position: 0, length: 12 });
  const b = new Uint8Array(file.data); const at = (offset, values) => values.every((v, i) => b[offset + i] === v);
  const type = at(0, [137, 80, 78, 71, 13, 10, 26, 10]) ? ['png', 'image/png'] : at(0, [255, 216, 255]) ? ['jpg', 'image/jpeg'] :
    at(0, [82, 73, 70, 70]) && at(8, [87, 69, 66, 80]) ? ['webp', 'image/webp'] : null;
  if (!type || !policy.allowedContentTypes.includes(type[1])) throw error('IMAGE_INVALID');
  return { contentHash: info.digest.toLowerCase(), size: info.size, extension: type[0], contentType: type[1] };
}
function matches(actual, expected) { return actual.contentHash === expected.contentHash && actual.size === expected.size && actual.extension === expected.extension && actual.contentType === expected.contentType; }
async function retain(platform, source, operationId, expected, policy) {
  const actual = await inspect(platform, source, policy); if (!matches(actual, expected)) throw error('IMAGE_MISMATCH');
  const destination = imagePath(platform, operationId, expected.extension);
  if (source !== destination) await call(platform.getFileSystemManager(), 'copyFile', { srcPath: source, destPath: destination });
  const saved = await inspect(platform, destination, policy); if (!matches(saved, expected)) throw error('IMAGE_MISMATCH'); return destination;
}
module.exports = { inspect, retain };
