const { isGuid, time } = require('./assessment');
const { actor } = require('./feedback');
const statuses = ['待本人确认', '已确认加入', '已拒绝', '学校已撤回', '已过期'];
const reasons = ['身份信息不符', '学校或角色不符', '暂不加入'];
const invalid = () => { throw Object.assign(new Error('INVALID_RESPONSE'), { code: 'INVALID_RESPONSE' }); };
const allowed = (state) => !!actor(state);
function invitation(row, state, expectedId) {
  if (!allowed(state) || !row || ![row.id,row.schoolId,row.targetUserId].every(isGuid) || expectedId && row.id !== expectedId || row.targetUserId !== state.context.accountUserId ||
      ![1,2].includes(row.role) || ![0,1,2,3,4].includes(row.status) || typeof row.schoolName !== 'string' || !row.schoolName.trim() || typeof row.schoolCode !== 'string' || !row.schoolCode.trim() || typeof row.concurrencyStamp !== 'string' || !row.concurrencyStamp || row.concurrencyStamp.length > 40 ||
      typeof row.canRespond !== 'boolean' || row.canRespond && row.status !== 0 || ![row.recordedAtUtc,row.effectiveFromUtc,row.expiresAtUtc].every(x=>Number.isFinite(Date.parse(x))) ||
      Date.parse(row.expiresAtUtc) <= Date.parse(row.recordedAtUtc) || row.effectiveToUtc != null && (!Number.isFinite(Date.parse(row.effectiveToUtc)) || Date.parse(row.effectiveToUtc) <= Date.parse(row.effectiveFromUtc)) ||
      (row.status === 1 ? !isGuid(row.membershipId) : row.membershipId != null) || ([1,2].includes(row.status) ? !isGuid(row.responseOperationId) : row.responseOperationId != null) ||
      ([1,2,3].includes(row.status) ? !Number.isFinite(Date.parse(row.completedAtUtc)) : row.completedAtUtc != null) ||
      (row.status === 2 ? ![0,1,2].includes(row.declineReason) : row.declineReason != null)) invalid();
  return { id:row.id,schoolId:row.schoolId,school:row.schoolName,schoolCode:row.schoolCode,role:row.role,roleName:row.role===1?'教师':'学生',status:row.status,statusName:statuses[row.status],
    from:time(row.effectiveFromUtc),to:row.effectiveToUtc?time(row.effectiveToUtc):'未指定结束时间',expires:time(row.expiresAtUtc),recorded:time(row.recordedAtUtc),
    canRespond:row.canRespond,stamp:row.concurrencyStamp,membershipId:row.membershipId||null,responseOperationId:row.responseOperationId||null,
    declineReason:row.declineReason==null?null:row.declineReason,declineText:row.declineReason==null?'':reasons[row.declineReason] };
}
function page(dto,state) {
  if (!dto || !Number.isSafeInteger(dto.totalCount) || dto.totalCount < 0 || !Array.isArray(dto.items) || dto.items.length>20) invalid();
  return { items:dto.items.map(x=>invitation(x,state)),total:dto.totalCount };
}
module.exports={allowed,invitation,page,reasons};
