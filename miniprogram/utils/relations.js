const {isGuid,time}=require('./assessment');
const {actor}=require('./feedback');
const invalid=()=>{throw Object.assign(new Error('INVALID_RESPONSE'),{code:'INVALID_RESPONSE'});};
const allowed=(s,school)=>!!actor(s)&&isGuid(school)&&s.context.workspaces.some(w=>w.role===1&&w.schoolId===school);
function relation(row){
  if(!row||!isGuid(row.id)||!['academicTermName','className','subjectName'].every(k=>typeof row[k]==='string')||!Number.isFinite(Date.parse(row.effectiveFromUtc))||row.effectiveToUtc!=null&&!Number.isFinite(Date.parse(row.effectiveToUtc)))invalid();
  return{id:row.id,title:`${row.academicTermName} · ${row.className} · ${row.subjectName}`,from:time(row.effectiveFromUtc),to:row.effectiveToUtc?time(row.effectiveToUtc):'未指定结束'};
}
function request(row,state,school,expectedId){
  if(!allowed(state,school)||!row||!isGuid(row.id)||expectedId&&row.id!==expectedId||row.schoolId!==school||row.teacherUserId!==state.context.accountUserId||
    !['schoolName','schoolCode','text','concurrencyStamp'].every(k=>typeof row[k]==='string'&&row[k].length>0)||row.concurrencyStamp.length>40||!Number.isFinite(Date.parse(row.recordedAtUtc))||
    ![0,1,2].includes(row.status)||typeof row.originalChanged!=='boolean'||typeof row.canWithdraw!=='boolean'||row.canWithdraw&&row.status!==0||
    (row.status===0?row.completionOperationId!=null||row.completedAtUtc!=null:!isGuid(row.completionOperationId)||!Number.isFinite(Date.parse(row.completedAtUtc))||!isGuid(row.completedByUserId))||
    (row.status===1?![0,1,2,3].includes(row.resolution)||typeof row.resolutionText!=='string':row.resolution!=null||row.resolutionText!=null)||
    row.originalAssignmentId!=null&&!isGuid(row.originalAssignmentId)||row.resultingAssignmentId!=null&&!isGuid(row.resultingAssignmentId))invalid();
  const original=row.original?relation(row.original):null,resulting=row.resulting?relation(row.resulting):null;
  if(original&&original.id!==row.originalAssignmentId||resulting&&resulting.id!==row.resultingAssignmentId||row.resultingAssignmentId&&!resulting||row.originalAssignmentId&&!original&&!row.originalChanged)invalid();
  return{id:row.id,school:row.schoolName,schoolCode:row.schoolCode,status:row.status,state:['待学校核验','学校已处理','本人已撤回'][row.status],text:row.text,at:time(row.recordedAtUtc),stamp:row.concurrencyStamp,
    original,originalChanged:row.originalChanged,resolution:row.resolution??null,resolutionText:row.resolutionText||'',resulting,canWithdraw:row.canWithdraw,completionOperationId:row.completionOperationId||null,completedByUserId:row.completedByUserId||null};
}
function page(dto,project){if(!dto||!Number.isSafeInteger(dto.totalCount)||dto.totalCount<0||!Array.isArray(dto.items)||dto.items.length>20)invalid();return{items:dto.items.map(project),total:dto.totalCount};}
module.exports={allowed,relation,request,page};
