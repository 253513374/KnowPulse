const model=require('./relations'),{actor,uuid}=require('./feedback'),{isGuid}=require('./assessment');
const error=code=>Object.assign(new Error(code),{code});
module.exports=function relationCommand(platform,session,config,school,active){
  const owner=actor(session.getState());if(!owner||!isGuid(school))throw error('AUTH_REQUIRED');
  const key=`knowpulse:relation-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${school}`;
  let pending=null,payload=null,busy=false;
  try{const row=platform.getStorageSync(key);if(row){if(row.schoolId!==school||!isGuid(row.requestId)||!isGuid(row.operationId)||![0,1].includes(row.kind)||row.kind===0&&row.requestId!==row.operationId)throw error('LOCAL_STATE_INVALID');pending={schoolId:school,requestId:row.requestId,operationId:row.operationId,kind:row.kind};}}catch(e){if(e.code==='LOCAL_STATE_INVALID')throw e;throw error('STORAGE_REQUIRED');}
  const current=()=>{if(actor(session.getState())!==owner||!active()||!model.allowed(session.getState(),school))throw error('STALE_RESPONSE');};
  function complete(dto){current();if(!pending)throw error('INVALID_RESPONSE');const row=model.request(dto,session.getState(),school,pending.requestId);
    if(pending.kind===0?row.id!==pending.operationId||payload&&(row.text!==payload.text.trim()||(dto.originalAssignmentId||null)!==(payload.originalAssignmentId||null)):row.status!==2||row.completionOperationId!==pending.operationId||row.completedByUserId!==session.getState().context.accountUserId)throw error('INVALID_RESPONSE');
    try{platform.removeStorageSync(key);}catch(_){throw error('STORAGE_REQUIRED');}pending=null;payload=null;return row;}
  async function recover(){current();if(!pending)return null;const dto=await session.request(`/api/app/education-school/my-relation-request/${pending.requestId}`);current();const row=model.request(dto,session.getState(),school,pending.requestId);return pending.kind===0||row.completionOperationId===pending.operationId?complete(dto):null;}
  async function send(){current();if(!pending||!payload)throw error('RETRY_BODY_LOST');return complete(await session.request(pending.kind===0?`/api/app/education-school/submit-relation-request/${school}`:`/api/app/education-school/withdraw-my-relation-request/${pending.requestId}`,'POST',payload));}
  return{state:()=>({pending:pending?{...pending}:null,canRetry:!!payload,busy}),dropPayload(){payload=null;},
    async start(kind,requestId,input){current();if(busy)throw error('BUSY');if(pending)throw error('PENDING_OPERATION');if(![0,1].includes(kind)||kind===1&&!isGuid(requestId)||kind===0&&(!input.confirmSharingWithSchool||typeof input.text!=='string'||!input.text.trim()||input.text.length>2000||input.originalAssignmentId!=null&&!isGuid(input.originalAssignmentId)))throw error('INVALID_PATH');busy=true;
      try{const op=await uuid(platform);current();const ref={schoolId:school,requestId:kind===0?op:requestId,operationId:op,kind};try{platform.setStorageSync(key,ref);}catch(_){throw error('STORAGE_REQUIRED');}pending=ref;payload=kind===0?{operationId:op,originalAssignmentId:input.originalAssignmentId||null,text:input.text,confirmSharingWithSchool:true}:{operationId:op,concurrencyStamp:input.concurrencyStamp};return await send();}finally{busy=false;}},
    async recover(){if(busy)throw error('BUSY');busy=true;try{return await recover();}finally{busy=false;}},
    async retry(){current();if(busy)throw error('BUSY');busy=true;try{return await recover()||await send();}finally{busy=false;}},
    abandon(){current();if(busy)throw error('BUSY');try{platform.removeStorageSync(key);}catch(_){throw error('STORAGE_REQUIRED');}pending=null;payload=null;},
  };
};
