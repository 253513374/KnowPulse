const model=require('./invitations');
const { actor,uuid }=require('./feedback');
const { isGuid }=require('./assessment');
const error=code=>Object.assign(new Error(code),{code});

module.exports=function invitationCommand(platform,session,config,invitationId,active) {
  const owner=actor(session.getState());
  if (!owner || !isGuid(invitationId)) throw error('AUTH_REQUIRED');
  const key=`knowpulse:invitation-op:${encodeURIComponent(config.apiBaseUrl)}:${config.appKey}:${config.clientId}:${owner}:${invitationId}`;
  let pending=null,payload=null,busy=false;
  try { const raw=platform.getStorageSync(key); if(raw) {
    if(raw.invitationId!==invitationId || !isGuid(raw.operationId) || ![0,1].includes(raw.response)) throw error('LOCAL_STATE_INVALID');
    pending={invitationId,operationId:raw.operationId,response:raw.response};
  }} catch(e) { if(e.code==='LOCAL_STATE_INVALID') throw e; throw error('STORAGE_REQUIRED'); }
  const requireCurrent=()=>{if(actor(session.getState())!==owner || !active() || !model.allowed(session.getState())) throw error('STALE_RESPONSE');};
  function complete(dto) {
    requireCurrent();const row=model.invitation(dto,session.getState(),invitationId);
    if(!pending || row.responseOperationId!==pending.operationId || row.status!==(pending.response===0?1:2) || payload && row.declineReason!==(payload.declineReason??null)) throw error('INVALID_RESPONSE');
    try{platform.removeStorageSync(key);}catch(_){throw error('STORAGE_REQUIRED');}
    pending=null;payload=null;return row;
  }
  async function recover() {
    requireCurrent();if(!pending)return null;
    const dto=await session.request(`/api/app/education-school/my-invitation/${invitationId}`);requireCurrent();
    const row=model.invitation(dto,session.getState(),invitationId);
    return row.responseOperationId===pending.operationId?complete(dto):null;
  }
  async function send() { requireCurrent();if(!pending || !payload)throw error('RETRY_BODY_LOST');return complete(await session.request(`/api/app/education-school/respond-to-invitation/${invitationId}`,'POST',payload)); }
  return {
    state:()=>({pending:pending?{...pending}:null,canRetry:!!payload,busy}),dropPayload(){payload=null;},
    async start(input) {
      requireCurrent();if(busy)throw error('BUSY');if(pending)throw error('PENDING_OPERATION');
      if(![0,1].includes(input.response) || typeof input.concurrencyStamp!=='string' || !input.concurrencyStamp || input.response===0 && (!input.confirmIdentityAndSchool || input.declineReason!=null) || input.response===1 && (![0,1,2].includes(input.declineReason)||input.confirmIdentityAndSchool))throw error('INVALID_PATH');
      busy=true;
      try { const operationId=await uuid(platform);requireCurrent();const ref={invitationId,operationId,response:input.response};
        try{platform.setStorageSync(key,ref);}catch(_){throw error('STORAGE_REQUIRED');}
        pending=ref;payload={operationId,concurrencyStamp:input.concurrencyStamp,response:input.response,declineReason:input.declineReason??null,confirmIdentityAndSchool:input.confirmIdentityAndSchool===true};
        return await send();
      }finally{busy=false;}
    },
    async recover(){if(busy)throw error('BUSY');busy=true;try{return await recover();}finally{busy=false;}},
    async retry(){requireCurrent();if(busy)throw error('BUSY');busy=true;try{const row=await recover();return row||await send();}finally{busy=false;}},
    abandon(){requireCurrent();if(busy)throw error('BUSY');try{platform.removeStorageSync(key);}catch(_){throw error('STORAGE_REQUIRED');}pending=null;payload=null;},
  };
};
