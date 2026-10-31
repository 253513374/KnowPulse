const { error } = require('./notebook');
const { notebookFilePath } = require('./answer-store');
const call = (target, method, options) => new Promise((resolve, reject) => target[method]({ ...options, success: resolve, fail: () => reject(error('FILE_UNAVAILABLE')) }));
const types = { pdf: 'application/pdf', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  xls: 'application/vnd.ms-excel', xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', ppt: 'application/vnd.ms-powerpoint', pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', txt: 'text/plain' };
function name(value) { if (typeof value !== 'string' || !value.trim() || value.length > 256 || /[\x00-\x1f\x7f/\\]/.test(value)) throw error('FILE_INVALID'); return value; }
async function inspect(platform, path, filename) {
  name(filename); const extension = filename.includes('.') ? filename.split('.').pop().toLowerCase() : 'bin';
  if (!/^[a-z0-9]{1,10}$/.test(extension)) throw error('FILE_INVALID');
  const info = await call(platform, 'getFileInfo', { filePath: path, digestAlgorithm: 'sha256' });
  if (!Number.isSafeInteger(info.size) || info.size < 1 || info.size > 20 * 1024 * 1024 || !/^[a-f0-9]{64}$/i.test(info.digest)) throw error('FILE_INVALID');
  return { size: info.size, contentHash: info.digest.toLowerCase(), extension, contentType: types[extension] || 'application/octet-stream' };
}
async function retain(platform, path, op, expected, filename) {
  const matches = (a) => ['size', 'contentHash', 'extension', 'contentType'].every((k) => a[k] === expected[k]);
  if (!matches(await inspect(platform, path, filename))) throw error('FILE_MISMATCH');
  const destination = notebookFilePath(platform, op, expected.extension);
  if (path !== destination) await call(platform.getFileSystemManager(), 'copyFile', { srcPath: path, destPath: destination });
  if (!matches(await inspect(platform, destination, filename))) throw error('FILE_MISMATCH'); return destination;
}
module.exports = { inspect, retain, name };
